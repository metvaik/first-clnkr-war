namespace FirstClnkrWar.Sim;

public static class Pathfinder
{
    private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
    private static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

    public static List<Cell> Find(SimMap map, UnitDomain domain, int sx, int sy, int gx, int gy)
    {
        var empty = new List<Cell>();
        if (!map.Walkable(sx, sy, domain))
            return empty;

        if (!map.Walkable(gx, gy, domain))
        {
            if (!TryNearestWalkable(map, domain, gx, gy, out gx, out gy))
                return empty;
        }

        int w = map.Width;
        int h = map.Height;
        int n = w * h;
        var gScore = new int[n];
        var parent = new int[n];
        Array.Fill(gScore, int.MaxValue);
        Array.Fill(parent, -1);

        int start = sy * w + sx;
        int goal = gy * w + gx;
        gScore[start] = 0;

        var heap = new Heap(256);
        int tie = 0;
        heap.Push(Heuristic(sx, sy, gx, gy), 0, tie++, start);

        var closed = new bool[n];
        while (heap.TryPop(out int poppedG, out int poppedF, out int current))
        {
            if (closed[current] || poppedG != gScore[current])
                continue;
            if (current == goal)
                return Reconstruct(parent, current, w);

            closed[current] = true;
            int cx = current % w;
            int cy = current / w;

            for (int d = 0; d < 8; d++)
            {
                int nx = cx + Dx[d];
                int ny = cy + Dy[d];
                if (!map.Walkable(nx, ny, domain))
                    continue;
                bool diagonal = Dx[d] != 0 && Dy[d] != 0;
                if (diagonal && (!map.Walkable(cx + Dx[d], cy, domain) || !map.Walkable(cx, cy + Dy[d], domain)))
                    continue;

                int ni = ny * w + nx;
                if (closed[ni])
                    continue;
                int step = diagonal ? 14 : 10;
                int nextG = gScore[current] + step;
                if (nextG >= gScore[ni])
                    continue;
                gScore[ni] = nextG;
                parent[ni] = current;
                int f = nextG + Heuristic(nx, ny, gx, gy);
                heap.Push(f, nextG, tie++, ni);
            }
        }

        return empty;
    }

    private static int Heuristic(int x, int y, int gx, int gy)
    {
        int dx = Math.Abs(x - gx);
        int dy = Math.Abs(y - gy);
        int min = Math.Min(dx, dy);
        int max = Math.Max(dx, dy);
        return 10 * max + 4 * min;
    }

    private static bool TryNearestWalkable(SimMap map, UnitDomain domain, int gx, int gy, out int ox, out int oy)
    {
        int maxR = Math.Max(map.Width, map.Height);
        for (int r = 1; r < maxR; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                        continue;
                    int x = gx + dx;
                    int y = gy + dy;
                    if (map.Walkable(x, y, domain))
                    {
                        ox = x;
                        oy = y;
                        return true;
                    }
                }
            }
        }

        ox = gx;
        oy = gy;
        return false;
    }

    private static List<Cell> Reconstruct(int[] parent, int current, int w)
    {
        var cells = new List<Cell>();
        int guard = parent.Length + 1;
        while (current >= 0 && guard-- > 0)
        {
            cells.Add(new Cell(current % w, current / w));
            current = parent[current];
        }
        cells.Reverse();
        return cells;
    }

    private sealed class Heap
    {
        private int[] _f;
        private int[] _g;
        private int[] _tie;
        private int[] _idx;
        private int _n;

        public Heap(int capacity)
        {
            _f = new int[capacity];
            _g = new int[capacity];
            _tie = new int[capacity];
            _idx = new int[capacity];
        }

        public void Push(int f, int g, int tie, int idx)
        {
            if (_n == _f.Length)
                Grow();
            int i = _n++;
            _f[i] = f;
            _g[i] = g;
            _tie[i] = tie;
            _idx[i] = idx;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (Compare(i, p) >= 0)
                    break;
                Swap(i, p);
                i = p;
            }
        }

        public bool TryPop(out int g, out int f, out int idx)
        {
            if (_n == 0)
            {
                g = 0;
                f = 0;
                idx = 0;
                return false;
            }

            f = _f[0];
            g = _g[0];
            idx = _idx[0];
            _n--;
            if (_n > 0)
            {
                _f[0] = _f[_n];
                _g[0] = _g[_n];
                _tie[0] = _tie[_n];
                _idx[0] = _idx[_n];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1;
                    int r = l + 1;
                    int s = i;
                    if (l < _n && Compare(l, s) < 0)
                        s = l;
                    if (r < _n && Compare(r, s) < 0)
                        s = r;
                    if (s == i)
                        break;
                    Swap(i, s);
                    i = s;
                }
            }

            return true;
        }

        private int Compare(int a, int b)
        {
            int c = _f[a].CompareTo(_f[b]);
            if (c != 0)
                return c;
            return _tie[a].CompareTo(_tie[b]);
        }

        private void Swap(int a, int b)
        {
            (_f[a], _f[b]) = (_f[b], _f[a]);
            (_g[a], _g[b]) = (_g[b], _g[a]);
            (_tie[a], _tie[b]) = (_tie[b], _tie[a]);
            (_idx[a], _idx[b]) = (_idx[b], _idx[a]);
        }

        private void Grow()
        {
            int size = _f.Length * 2;
            Array.Resize(ref _f, size);
            Array.Resize(ref _g, size);
            Array.Resize(ref _tie, size);
            Array.Resize(ref _idx, size);
        }
    }
}

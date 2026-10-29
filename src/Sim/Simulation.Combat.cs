namespace FirstClnkrWar.Sim;

public sealed partial class Simulation
{
    private void TickProjectiles()
    {
        int count = _entities.Count;
        for (int i = 0; i < count; i++)
        {
            var shot = _entities[i];
            if (!shot.Alive || shot.Kind != EntityKind.Projectile)
                continue;
            if (!_byId.TryGetValue(shot.ProjectileTargetId, out var target) || !target.Alive)
            {
                shot.Alive = false;
                continue;
            }

            long dx = target.X.Raw - shot.X.Raw;
            long dy = target.Y.Raw - shot.Y.Raw;
            long dist2 = dx * dx + dy * dy;
            long step = shot.ProjectileStepRaw;
            if (step < 1)
                step = 1;
            if (dist2 <= step * step)
            {
                int armour = 0;
                if ((uint)target.PlayerId < (uint)Players.Length)
                    armour = Players[target.PlayerId].Armour;
                int amount = DamageMath.Apply(shot.PayloadDamage, shot.PayloadFirepower, armour);
                ApplyDamage(target, amount);
                shot.Alive = false;
            }
            else
            {
                long dist = IntMath.Isqrt(dist2);
                if (dist < 1)
                    dist = 1;
                shot.X = new Fp(shot.X.Raw + dx * step / dist);
                shot.Y = new Fp(shot.Y.Raw + dy * step / dist);
                ClampToMap(shot);
                SyncCell(shot);
            }
        }
    }

    private void TickWeapons()
    {
        int interval = Data.Rules.TargetSearchIntervalTicks;
        int count = _entities.Count;
        for (int i = 0; i < count; i++)
        {
            var entity = _entities[i];
            if (!entity.Alive || entity.Kind != EntityKind.Unit)
                continue;
            var def = Def(entity);
            if (def.Weapons.Count == 0)
                continue;

            bool search = (entity.Id + Tick) % interval == 0;
            if (search)
                AcquireTarget(entity, def);

            if (entity.SalvoLeft > 0)
            {
                if (entity.SalvoWait > 0)
                {
                    entity.SalvoWait--;
                    continue;
                }
                if (!_byId.TryGetValue(entity.TargetId, out var salvoTarget) || !salvoTarget.Alive
                    || !CanSee(entity.PlayerId, salvoTarget)
                    || entity.SalvoWeapon < 0
                    || entity.SalvoWeapon >= def.Weapons.Count
                    || !InRange(entity, salvoTarget, def.Weapons[entity.SalvoWeapon]))
                {
                    entity.SalvoLeft = 0;
                    entity.SalvoWeapon = -1;
                    continue;
                }
                SpawnProjectile(entity, salvoTarget, def.Weapons[entity.SalvoWeapon]);
                entity.SalvoLeft--;
                if (entity.SalvoLeft > 0)
                    entity.SalvoWait = SecondsToTicks(def.Weapons[entity.SalvoWeapon].SalvoInterval);
                else
                    entity.WeaponCooldown[entity.SalvoWeapon] = SecondsToTicks(def.Weapons[entity.SalvoWeapon].Cooldown);
                continue;
            }

            for (int w = 0; w < def.Weapons.Count && w < entity.WeaponCooldown.Length; w++)
            {
                if (entity.WeaponCooldown[w] > 0)
                    entity.WeaponCooldown[w]--;
            }

            if (!_byId.TryGetValue(entity.TargetId, out var target) || !target.Alive)
                continue;
            if (!CanSee(entity.PlayerId, target))
                continue;

            for (int w = 0; w < def.Weapons.Count && w < entity.WeaponCooldown.Length; w++)
            {
                var weapon = def.Weapons[w];
                if (entity.WeaponCooldown[w] > 0)
                    continue;
                if (!weapon.CanHit(target.Domain, target.Kind == EntityKind.Building))
                    continue;
                if (!InRange(entity, target, weapon))
                    continue;
                entity.SalvoWeapon = w;
                entity.SalvoLeft = weapon.Salvo;
                entity.SalvoWait = 0;
                SpawnProjectile(entity, target, weapon);
                entity.SalvoLeft--;
                if (entity.SalvoLeft > 0)
                    entity.SalvoWait = SecondsToTicks(weapon.SalvoInterval);
                else
                    entity.WeaponCooldown[w] = SecondsToTicks(weapon.Cooldown);
                break;
            }
        }
    }

    private void AcquireTarget(SimEntity entity, UnitDef def)
    {
        if (entity.Order == OrderKind.Move)
        {
            entity.TargetId = 0;
            return;
        }

        if (entity.Order == OrderKind.Attack)
        {
            if (_byId.TryGetValue(entity.TargetId, out var locked) && locked.Alive && locked.PlayerId != entity.PlayerId)
                return;
            entity.TargetId = 0;
            entity.Order = OrderKind.Idle;
        }

        SimEntity? best = null;
        long bestDist = long.MaxValue;
        for (int i = 0; i < _entities.Count; i++)
        {
            var other = _entities[i];
            if (!IsEnemyTarget(entity, def, other))
                continue;
            if (!CanSee(entity.PlayerId, other))
                continue;
            if (!InAnyWeaponRange(entity, other))
                continue;
            long d = WorldScale.DistanceSquared(entity.X, entity.Y, other.X, other.Y);
            if (best == null || d < bestDist || (d == bestDist && other.Id < best.Id))
            {
                best = other;
                bestDist = d;
            }
        }

        entity.TargetId = best?.Id ?? 0;
    }

    private bool IsEnemyTarget(SimEntity self, UnitDef def, SimEntity other)
    {
        if (!other.Alive || other.Id == self.Id || other.PlayerId < 0 || other.PlayerId == self.PlayerId)
            return false;
        if (other.Kind != EntityKind.Unit && other.Kind != EntityKind.Building)
            return false;
        for (int w = 0; w < def.Weapons.Count; w++)
        {
            if (def.Weapons[w].CanHit(other.Domain, other.Kind == EntityKind.Building))
                return true;
        }
        return false;
    }

    private bool InAnyWeaponRange(SimEntity self, SimEntity target)
    {
        var def = Def(self);
        for (int w = 0; w < def.Weapons.Count; w++)
        {
            var weapon = def.Weapons[w];
            if (!weapon.CanHit(target.Domain, target.Kind == EntityKind.Building))
                continue;
            if (InRange(self, target, weapon))
                return true;
        }
        return false;
    }

    private bool InRange(SimEntity self, SimEntity target, WeaponDef weapon)
    {
        Fp range = DamageMath.ApplyRangeBonus(weapon.Range, 0, Data.Rules.RangeBonusMaxPct);
        return WorldScale.WithinTiles(self.X, self.Y, target.X, target.Y, range);
    }

    private void SpawnProjectile(SimEntity source, SimEntity target, WeaponDef weapon)
    {
        if (_projectileCount >= Data.Rules.ProjectilePool)
            return;
        int firepower = 0;
        if ((uint)source.PlayerId < (uint)Players.Length)
            firepower = Players[source.PlayerId].Firepower;
        var shot = new SimEntity
        {
            Id = _nextId++,
            Kind = EntityKind.Projectile,
            PlayerId = source.PlayerId,
            TypeId = "",
            Domain = source.Domain,
            Shape = "tracer",
            Role = "projectile",
            X = source.X,
            Y = source.Y,
            PrevX = source.X,
            PrevY = source.Y,
            Alive = true,
            Hp = 1,
            MaxHp = 1,
            ProjectileTargetId = target.Id,
            PayloadDamage = weapon.DamageAgainst(target.Domain),
            PayloadFirepower = firepower,
            ProjectileStepRaw = Math.Max(1, WorldScale.WorldPerTickRaw(weapon.ProjectileSpeed, Data.Rules.TickHz)),
            CellX = source.CellX,
            CellY = source.CellY,
        };
        _entities.Add(shot);
        _byId[shot.Id] = shot;
        _projectileCount++;
    }

    private void ApplyDamage(SimEntity target, int amount)
    {
        if (!target.Alive || target.Kind == EntityKind.Deposit || target.Kind == EntityKind.Projectile)
            return;
        target.Hp -= amount;
        if (target.Hp > 0)
            return;
        target.Hp = 0;
        target.Alive = false;
        target.Order = OrderKind.Idle;
        target.Path.Clear();
        target.PathPending = false;
        Unblock(target);
    }

    private void RemoveDeadProjectiles()
    {
        for (int i = _entities.Count - 1; i >= 0; i--)
        {
            var e = _entities[i];
            if (e.Kind == EntityKind.Projectile && !e.Alive)
            {
                _byId.Remove(e.Id);
                _entities.RemoveAt(i);
                _projectileCount--;
            }
        }
    }
}

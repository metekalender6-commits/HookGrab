using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;

namespace HookGrab;

public class HookGrabPlugin : BasePlugin
{
    public override string ModuleName => "Hook & Grab";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "you";
    public override string ModuleDescription => "css_hook (@css/ban) ve css_grab (@css/root) + trail + hold";

    private readonly FakeConVar<float> _hookSpeed = new("css_hook_speed", "Hook fırlatma hızı", 900f);
    private readonly FakeConVar<float> _hookUpBoost = new("css_hook_upboost", "Hook'ta yukarı ekstra itiş", 100f);
    private readonly FakeConVar<float> _grabMaxDistance = new("css_grab_max_distance", "Grab hedefi ararken max mesafe", 2000f);
    private readonly FakeConVar<float> _grabMinDist = new("css_grab_min_dist", "Grab'da en yakın mesafe", 40f);
    private readonly FakeConVar<float> _grabMaxDist = new("css_grab_max_dist", "Grab'da en uzak mesafe", 500f);
    private readonly FakeConVar<float> _grabMoveSpeed = new("css_grab_move_speed", "Space/Ctrl ile mesafe değişim hızı (tick başı)", 6f);
    private readonly FakeConVar<float> _trailLength = new("css_hook_trail_length", "Hook trail uzunluğu", 800f);
    private readonly FakeConVar<float> _beamWidth = new("css_hook_beam_width", "Beam kalınlığı", 2.5f);

    // slot -> state
    private readonly Dictionary<int, HookState> _hooks = new();
    private readonly Dictionary<int, GrabState> _grabs = new();

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnTick>(OnTick);
    }

    public override void Unload(bool hotReload)
    {
        foreach (var h in _hooks.Values)
            RemoveBeam(h.Beam);
        foreach (var g in _grabs.Values)
            RemoveBeam(g.Beam);
        _hooks.Clear();
        _grabs.Clear();
    }

    // ---------------- HOOK (hold + trail) ----------------

    [ConsoleCommand("css_hook", "Baktığın yöne hook atar (tekrar basınca / tuş bırakınca biter)")]
    [RequiresPermissions("@css/ban")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnHookCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        // Zaten aktifse bırak
        if (_hooks.TryGetValue(player.Slot, out var existing))
        {
            RemoveBeam(existing.Beam);
            _hooks.Remove(player.Slot);
            player.PrintToChat(" \x04[Hook]\x01 Bıraktın.");
            return;
        }

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;
        if (pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

        var beam = CreateBeam(Color.FromArgb(255, 0, 255, 255)); // başlangıç rengi
        _hooks[player.Slot] = new HookState
        {
            Beam = beam,
            StartTime = Server.CurrentTime
        };

        player.PrintToChat($" \x04[Hook]\x01 Aktif! (hız: {_hookSpeed.Value:0}) Tekrar !hook = bırak. Trail gittiğin yöne çiziliyor.");
    }

    [ConsoleCommand("css_hookspeed", "Hook hızını ayarlar")]
    [RequiresPermissions("@css/ban")]
    [CommandHelper(minArgs: 1, usage: "<hız>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnHookSpeedCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null) return;

        if (float.TryParse(command.GetArg(1), out float val) && val > 0)
        {
            _hookSpeed.Value = val;
            player.PrintToChat($" \x04[Hook]\x01 Hook hızı {val:0} olarak ayarlandı.");
        }
        else
        {
            player.PrintToChat(" \x02[Hook]\x01 Geçersiz değer. Kullanım: css_hookspeed <sayı>");
        }
    }

    // ---------------- GRAB ----------------

    [ConsoleCommand("css_grab", "Baktığın oyuncuyu grablar / tekrar basınca bırakır")]
    [RequiresPermissions("@css/root")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnGrabCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        var existingEntry = _grabs.FirstOrDefault(kv => kv.Value.GrabberSlot == player.Slot);
        if (existingEntry.Value != null)
        {
            RemoveBeam(existingEntry.Value.Beam);
            _grabs.Remove(existingEntry.Key);
            var releasedPlayer = Utilities.GetPlayerFromSlot(existingEntry.Key);
            releasedPlayer?.PrintToChat(" \x04[Grab]\x01 Bırakıldın.");
            player.PrintToChat(" \x04[Grab]\x01 Bıraktın.");
            return;
        }

        var target = GetLookedAtPlayer(player, _grabMaxDistance.Value);
        if (target == null)
        {
            player.PrintToChat(" \x02[Grab]\x01 Şu an kimseye bakmıyorsun.");
            return;
        }

        if (_grabs.ContainsKey(target.Slot))
        {
            player.PrintToChat(" \x02[Grab]\x01 Bu oyuncu zaten başka biri tarafından grablanmış.");
            return;
        }

        var beam = CreateBeam(Color.FromArgb(255, 255, 50, 50));
        _grabs[target.Slot] = new GrabState
        {
            GrabberSlot = player.Slot,
            Distance = (_grabMinDist.Value + _grabMaxDist.Value) / 2f,
            Beam = beam,
            StartTime = Server.CurrentTime
        };

        player.PrintToChat($" \x04[Grab]\x01 {target.PlayerName} grablandı. Space: uzaklaştır, Ctrl: yaklaştır. Tekrar !grab: bırak.");
        target.PrintToChat(" \x02[Grab]\x01 Grablandın!");
    }

    private void OnTick()
    {
        float now = Server.CurrentTime;

        // ---- HOOK ----
        foreach (var kvp in _hooks.ToList())
        {
            int slot = kvp.Key;
            var state = kvp.Value;
            var player = Utilities.GetPlayerFromSlot(slot);

            if (!IsAliveAndValid(player))
            {
                RemoveBeam(state.Beam);
                _hooks.Remove(slot);
                continue;
            }

            var pawn = player!.PlayerPawn.Value!;
            var forward = AnglesToForward(pawn.EyeAngles);
            float speed = _hookSpeed.Value;

            // Sürekli velocity (hold etkisi)
            pawn.AbsVelocity.X = forward.X * speed;
            pawn.AbsVelocity.Y = forward.Y * speed;
            pawn.AbsVelocity.Z = forward.Z * speed + _hookUpBoost.Value;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_vecAbsVelocity");

            // Trail: göz hizasından bakış yönüne doğru
            var eyePos = new Vector(pawn.AbsOrigin!.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + 64f);
            var endPos = new Vector(
                eyePos.X + forward.X * _trailLength.Value,
                eyePos.Y + forward.Y * _trailLength.Value,
                eyePos.Z + forward.Z * _trailLength.Value
            );

            UpdateBeam(state.Beam, eyePos, endPos, GetRainbowColor(now - state.StartTime));
        }

        // ---- GRAB ----
        if (_grabs.Count == 0) return;

        foreach (var kvp in _grabs.ToList())
        {
            int targetSlot = kvp.Key;
            var state = kvp.Value;

            var grabber = Utilities.GetPlayerFromSlot(state.GrabberSlot);
            var target = Utilities.GetPlayerFromSlot(targetSlot);

            if (!IsAliveAndValid(grabber) || !IsAliveAndValid(target))
            {
                RemoveBeam(state.Beam);
                _grabs.Remove(targetSlot);
                continue;
            }

            var grabberPawn = grabber!.PlayerPawn.Value!;
            var targetPawn = target!.PlayerPawn.Value!;

            var buttons = grabber.Buttons;
            if ((buttons & PlayerButtons.Jump) != 0)
                state.Distance += _grabMoveSpeed.Value;
            if ((buttons & PlayerButtons.Duck) != 0)
                state.Distance -= _grabMoveSpeed.Value;

            state.Distance = Math.Clamp(state.Distance, _grabMinDist.Value, _grabMaxDist.Value);

            var eyePos = grabberPawn.AbsOrigin!;
            var forward = AnglesToForward(grabberPawn.EyeAngles);

            var newPos = new Vector(
                eyePos.X + forward.X * state.Distance,
                eyePos.Y + forward.Y * state.Distance,
                eyePos.Z + forward.Z * state.Distance + 64f
            );

            targetPawn.Teleport(newPos, targetPawn.AbsRotation, new Vector(0, 0, 0));

            // Grab beam: grabber gözü → target
            var beamStart = new Vector(eyePos.X, eyePos.Y, eyePos.Z + 64f);
            var beamEnd = new Vector(newPos.X, newPos.Y, newPos.Z);
            UpdateBeam(state.Beam, beamStart, beamEnd, GetRainbowColor(now - state.StartTime));
        }
    }

    // ---------------- BEAM / TRAIL ----------------

    private CBeam? CreateBeam(Color color)
    {
        var beam = Utilities.CreateEntityByName<CBeam>("env_beam");
        if (beam == null || !beam.IsValid) return null;

        beam.Render = color;
        beam.Width = _beamWidth.Value;
        beam.EndWidth = _beamWidth.Value;
        beam.DispatchSpawn();
        return beam;
    }

    private void UpdateBeam(CBeam? beam, Vector start, Vector end, Color color)
    {
        if (beam == null || !beam.IsValid) return;

        beam.Render = color;
        beam.Teleport(start, new QAngle(0, 0, 0), new Vector(0, 0, 0));
        beam.EndPos.X = end.X;
        beam.EndPos.Y = end.Y;
        beam.EndPos.Z = end.Z;
        Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");
        Utilities.SetStateChanged(beam, "CBaseModelEntity", "m_clrRender");
    }

    private static void RemoveBeam(CBeam? beam)
    {
        if (beam != null && beam.IsValid)
            beam.Remove();
    }

    private static Color GetRainbowColor(float t)
    {
        // Basit HSV → RGB döngüsü (renk değiştiren trail)
        float h = (t * 0.6f) % 1f; // hız ayarı
        float s = 1f, v = 1f;
        int i = (int)(h * 6);
        float f = h * 6 - i;
        float p = v * (1 - s);
        float q = v * (1 - f * s);
        float u = v * (1 - (1 - f) * s);

        float r, g, b;
        switch (i % 6)
        {
            case 0: r = v; g = u; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = u; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = u; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
        return Color.FromArgb(255, (int)(r * 255), (int)(g * 255), (int)(b * 255));
    }

    // ---------------- YARDIMCI ----------------

    private static bool IsAliveAndValid(CCSPlayerController? p)
    {
        return p != null && p.IsValid
            && p.PlayerPawn.Value != null && p.PlayerPawn.Value.IsValid
            && p.PlayerPawn.Value.AbsOrigin != null
            && p.PlayerPawn.Value.LifeState == (byte)LifeState_t.LIFE_ALIVE;
    }

    private CCSPlayerController? GetLookedAtPlayer(CCSPlayerController looker, float maxDistance)
    {
        var lookerPawn = looker.PlayerPawn.Value;
        if (lookerPawn == null || lookerPawn.AbsOrigin == null) return null;

        var eyePos = new Vector(lookerPawn.AbsOrigin.X, lookerPawn.AbsOrigin.Y, lookerPawn.AbsOrigin.Z + 64f);
        var forward = AnglesToForward(lookerPawn.EyeAngles);

        CCSPlayerController? best = null;
        float bestDot = 0.965f;

        foreach (var p in Utilities.GetPlayers())
        {
            if (p.Slot == looker.Slot || !IsAliveAndValid(p)) continue;

            var targetOrigin = p.PlayerPawn.Value!.AbsOrigin!;
            var toTarget = new Vector(
                targetOrigin.X - eyePos.X,
                targetOrigin.Y - eyePos.Y,
                targetOrigin.Z - eyePos.Z
            );

            float dist = MathF.Sqrt(toTarget.X * toTarget.X + toTarget.Y * toTarget.Y + toTarget.Z * toTarget.Z);
            if (dist > maxDistance || dist < 1f) continue;

            var dir = new Vector(toTarget.X / dist, toTarget.Y / dist, toTarget.Z / dist);
            float dot = dir.X * forward.X + dir.Y * forward.Y + dir.Z * forward.Z;

            if (dot > bestDot)
            {
                bestDot = dot;
                best = p;
            }
        }
        return best;
    }

    private static Vector AnglesToForward(QAngle angles)
    {
        float pitch = angles.X * (MathF.PI / 180f);
        float yaw = angles.Y * (MathF.PI / 180f);

        float cp = MathF.Cos(pitch);
        float sp = MathF.Sin(pitch);
        float cy = MathF.Cos(yaw);
        float sy = MathF.Sin(yaw);

        return new Vector(cp * cy, cp * sy, -sp);
    }

    private class HookState
    {
        public CBeam? Beam;
        public float StartTime;
    }

    private class GrabState
    {
        public int GrabberSlot;
        public float Distance;
        public CBeam? Beam;
        public float StartTime;
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace HookGrab;

public class HookGrabPlugin : BasePlugin
{
    public override string ModuleName => "Hook & Grab";
    public override string ModuleVersion => "1.4.0";
    public override string ModuleAuthor => "you";
    public override string ModuleDescription => "Hold Hook (CT + @css/ban) & Hold Grab (@css/root)";

    private readonly FakeConVar<float> _hookSpeed = new("css_hook_speed", "Hook fırlatma hızı", 900f);
    private readonly FakeConVar<float> _hookUpBoost = new("css_hook_upboost", "Hook'ta yukarı ekstra itiş", 100f);
    private readonly FakeConVar<float> _grabMaxDistance = new("css_grab_max_distance", "Grab hedefi ararken max mesafe", 2000f);
    private readonly FakeConVar<float> _grabMinDist = new("css_grab_min_dist", "Grab'da en yakın mesafe", 40f);
    private readonly FakeConVar<float> _grabMaxDist = new("css_grab_max_dist", "Grab'da en uzak mesafe", 500f);
    private readonly FakeConVar<float> _grabMoveSpeed = new("css_grab_move_speed", "Space/Ctrl ile mesafe değişim hızı (tick başı)", 6f);
    private readonly FakeConVar<float> _trailLength = new("css_hook_trail_length", "Hook trail uzunluğu", 800f);
    private readonly FakeConVar<float> _beamWidth = new("css_hook_beam_width", "Beam kalınlığı", 2.5f);

    private readonly Dictionary<int, HookState> _hooks = new();
    private readonly Dictionary<int, GrabState> _grabs = new();

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnTick>(OnTick);
    }

    public override void Unload(bool hotReload)
    {
        foreach (var h in _hooks.Values) RemoveBeam(h.Beam);
        foreach (var g in _grabs.Values) RemoveBeam(g.Beam);
        _hooks.Clear();
        _grabs.Clear();
    }

    // ==================== HOOK (CT + @css/ban) ====================

    [ConsoleCommand("css_hook_on", "Hook başlat (basılı tut)")]
    [RequiresPermissions("@css/ban")]
    [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnHookOn(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        if (player.Team != CsTeam.CounterTerrorist)
        {
            player.PrintToChat(" \x02[Hook]\x01 Sadece CT kullanabilir.");
            return;
        }

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;
        if (pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

        if (_hooks.ContainsKey(player.Slot)) return;

        var beam = CreateBeam(Color.FromArgb(255, 0, 200, 255));
        _hooks[player.Slot] = new HookState
        {
            Beam = beam,
            StartTime = Server.CurrentTime
        };
    }

    [ConsoleCommand("css_hook_off", "Hook bitir (tuş bırak)")]
    [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnHookOff(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        if (_hooks.TryGetValue(player.Slot, out var state))
        {
            RemoveBeam(state.Beam);
            _hooks.Remove(player.Slot);
        }
    }

    // ==================== GRAB (@css/root) ====================

    [ConsoleCommand("css_grab_on", "Grab başlat (basılı tut)")]
    [RequiresPermissions("@css/root")]
    [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnGrabOn(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        // Zaten birini tutuyorsa tekrar basınca bırak
        var existing = _grabs.FirstOrDefault(kv => kv.Value.GrabberSlot == player.Slot);
        if (existing.Value != null)
        {
            RemoveBeam(existing.Value.Beam);
            _grabs.Remove(existing.Key);
            var released = Utilities.GetPlayerFromSlot(existing.Key);
            released?.PrintToChat(" \x04[Grab]\x01 Bırakıldın.");
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
            player.PrintToChat(" \x02[Grab]\x01 Bu oyuncu zaten grablanmış.");
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

        player.PrintToChat($" \x04[Grab]\x01 {target.PlayerName} grablandı.");
        target.PrintToChat(" \x02[Grab]\x01 Grablandın!");
    }

    [ConsoleCommand("css_grab_off", "Grab bitir (tuş bırak)")]
    [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnGrabOff(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        var existing = _grabs.FirstOrDefault(kv => kv.Value.GrabberSlot == player.Slot);
        if (existing.Value != null)
        {
            RemoveBeam(existing.Value.Beam);
            _grabs.Remove(existing.Key);

            var released = Utilities.GetPlayerFromSlot(existing.Key);
            released?.PrintToChat(" \x04[Grab]\x01 Bırakıldın.");
            player.PrintToChat(" \x04[Grab]\x01 Bıraktın.");
        }
    }

    // ==================== KULLANIM KOMUTLARI ====================

    [ConsoleCommand("css_hookkodu", "Hook kullanım kodunu gösterir")]
    [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnHookKodu(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null) return;

        player.PrintToChat(" \x04========== HOOK KULLANIM ==========");
        player.PrintToChat(" \x01Konsola şunu yaz:");
        player.PrintToChat(" \x04alias +hook \"css_hook_on\"");
        player.PrintToChat(" \x04alias -hook \"css_hook_off\"");
        player.PrintToChat(" \x04bind tuş \"+hook\"");
        player.PrintToChat(" \x01Örnek: \x04bind f \"+hook\"");
        player.PrintToChat(" \x01Sadece \x04CT\x01 + \x04@css/ban\x01 yetkisi olanlar kullanabilir.");
        player.PrintToChat(" \x04===================================");
    }

    [ConsoleCommand("css_grabkodu", "Grab kullanım kodunu gösterir")]
    [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnGrabKodu(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null) return;

        player.PrintToChat(" \x04========== GRAB KULLANIM ==========");
        player.PrintToChat(" \x01Konsola şunu yaz:");
        player.PrintToChat(" \x04alias +grab \"css_grab_on\"");
        player.PrintToChat(" \x04alias -grab \"css_grab_off\"");
        player.PrintToChat(" \x04bind tuş \"+grab\"");
        player.PrintToChat(" \x01Örnek: \x04bind g \"+grab\"");
        player.PrintToChat(" \x01Sadece \x04@css/root\x01 yetkisi olanlar kullanabilir.");
        player.PrintToChat(" \x04===================================");
    }

    // Eski isimler de çalışsın
    [ConsoleCommand("css_hook", "Eski uyumluluk")]
    [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnHookOld(CCSPlayerController? player, CommandInfo command) => OnHookOn(player, command);

    [ConsoleCommand("css_grab", "Eski uyumluluk")]
    [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnGrabOld(CCSPlayerController? player, CommandInfo command) => OnGrabOn(player, command);

    // ==================== TICK ====================

    private void OnTick()
    {
        float now = Server.CurrentTime;

        // ---- HOOK ----
        foreach (var kvp in _hooks.ToList())
        {
            int slot = kvp.Key;
            var state = kvp.Value;
            var player = Utilities.GetPlayerFromSlot(slot);

            if (!IsAliveAndValid(player) || player!.Team != CsTeam.CounterTerrorist)
            {
                RemoveBeam(state.Beam);
                _hooks.Remove(slot);
                continue;
            }

            var pawn = player.PlayerPawn.Value!;
            var forward = AnglesToForward(pawn.EyeAngles);

            float speed = _hookSpeed.Value;
            pawn.AbsVelocity.X = forward.X * speed;
            pawn.AbsVelocity.Y = forward.Y * speed;
            pawn.AbsVelocity.Z = forward.Z * speed + _hookUpBoost.Value;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_vecAbsVelocity");

            var eyePos = new Vector(pawn.AbsOrigin!.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + 64f);
            var endPos = new Vector(
                eyePos.X + forward.X * _trailLength.Value,
                eyePos.Y + forward.Y * _trailLength.Value,
                eyePos.Z + forward.Z * _trailLength.Value
            );
            UpdateBeam(state.Beam, eyePos, endPos, GetRainbowColor(now - state.StartTime));
        }

        // ---- GRAB ----
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

            var beamStart = new Vector(eyePos.X, eyePos.Y, eyePos.Z + 64f);
            UpdateBeam(state.Beam, beamStart, newPos, GetRainbowColor(now - state.StartTime));
        }
    }

    // ==================== BEAM ====================

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
        float h = (t * 0.6f) % 1f;
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

    // ==================== YARDIMCI ====================

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

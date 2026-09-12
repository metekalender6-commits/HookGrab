using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HookGrab;

public class HookGrabPlugin : BasePlugin
{
    public override string ModuleName => "Hook & Grab";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "you";
    public override string ModuleDescription => "css_hook (@css/ban) ve css_grab (@css/root) komutları";

    // Ayarlanabilir değerler (server.cfg veya konsoldan değiştirilebilir)
    private readonly FakeConVar<float> _hookSpeed = new("css_hook_speed", "Hook fırlatma hızı", 900f);
    private readonly FakeConVar<float> _hookUpBoost = new("css_hook_upboost", "Hook'ta yukarı ekstra itiş", 100f);
    private readonly FakeConVar<float> _grabMaxDistance = new("css_grab_max_distance", "Grab hedefi ararken max mesafe", 2000f);
    private readonly FakeConVar<float> _grabMinDist = new("css_grab_min_dist", "Grab'da en yakın mesafe", 40f);
    private readonly FakeConVar<float> _grabMaxDist = new("css_grab_max_dist", "Grab'da en uzak mesafe", 500f);
    private readonly FakeConVar<float> _grabMoveSpeed = new("css_grab_move_speed", "Space/Ctrl ile mesafe değişim hızı (tick başı)", 6f);

    // target slot -> grab durumu
    private readonly Dictionary<int, GrabState> _grabs = new();

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnTick>(OnTick);
    }

    // ---------------- HOOK ----------------

    [ConsoleCommand("css_hook", "Baktığın yöne doğru fırlarsın (hook)")]
    [RequiresPermissions("@css/ban")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnHookCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;
        if (pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

        var forward = AnglesToForward(pawn.EyeAngles);
        float speed = _hookSpeed.Value;

        pawn.AbsVelocity.X = forward.X * speed;
        pawn.AbsVelocity.Y = forward.Y * speed;
        pawn.AbsVelocity.Z = forward.Z * speed + _hookUpBoost.Value;

        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_vecAbsVelocity");

        player.PrintToChat($" \x04[Hook]\x01 Fırlatıldın! (hız: {speed:0})");
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

        // Zaten birini grablamışsa, komutu tekrar çalıştırınca bırak
        var existingEntry = _grabs.FirstOrDefault(kv => kv.Value.GrabberSlot == player.Slot);
        if (existingEntry.Value != null)
        {
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

        _grabs[target.Slot] = new GrabState
        {
            GrabberSlot = player.Slot,
            Distance = (_grabMinDist.Value + _grabMaxDist.Value) / 2f
        };

        player.PrintToChat($" \x04[Grab]\x01 {target.PlayerName} grablandı. Space: uzaklaştır, Ctrl: yaklaştır. Tekrar !grab: bırak.");
        target.PrintToChat(" \x02[Grab]\x01 Grablandın!");
    }

    private void OnTick()
    {
        if (_grabs.Count == 0) return;

        foreach (var kvp in _grabs.ToList())
        {
            int targetSlot = kvp.Key;
            var state = kvp.Value;

            var grabber = Utilities.GetPlayerFromSlot(state.GrabberSlot);
            var target = Utilities.GetPlayerFromSlot(targetSlot);

            if (!IsAliveAndValid(grabber) || !IsAliveAndValid(target))
            {
                _grabs.Remove(targetSlot);
                continue;
            }

            var grabberPawn = grabber!.PlayerPawn.Value!;
            var targetPawn = target!.PlayerPawn.Value!;

            // Space -> uzaklaştır, Ctrl -> yaklaştır
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
                eyePos.Z + forward.Z * state.Distance + 64f // göz hizası offseti
            );

            targetPawn.Teleport(newPos, targetPawn.AbsRotation, new Vector(0, 0, 0));
        }
    }

    // ---------------- YARDIMCI FONKSİYONLAR ----------------

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
        float bestDot = 0.965f; // ~yaklaşık 15 derecelik koni; daraltmak/genişletmek için değiştir

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

    private class GrabState
    {
        public int GrabberSlot;
        public float Distance;
    }
}

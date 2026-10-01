# Fish-Easily

Fishing without the fight.

Valheim's fishing loop is a stamina tax wrapped in reaction timing: wait for a nibble,
hit a half second window, then hold the button through a tug of war while your green bar
drains and the line snaps. Fish-Easily keeps the cast and the wait and removes the rest.

Client-side. Nothing needs to be installed on the server, and nothing is spawned, added
or rebalanced. Every change is a patch to vanilla behaviour, and every one of them has
its own toggle in `BepInEx/config/valheim.fisheasily.cfg`.

## What it does

**One bait fits all**
Vanilla only lets a fish bite when the bait on your line appears in that fish's own bait
table, which is why you end up carrying five bait types for six species. With this on,
any bait catches any fish. Plain bait from Haldor is enough for everything.

**Bait not consumed**
The bait goes back into your inventory after every catch. One stack lasts forever.

**Fish come to you**
Half of a fish's swim targets ignore your line entirely by default, because vanilla rolls
`Random.value < m_baseHookChance` before deciding to approach. This removes the coin
flip, so the fish you can see are the fish that come to your float.

**Auto hook**
The fish is on the line the instant it nibbles. No reaction window, no missed hooks.

**No stamina drain**
Reeling, and holding a hooked fish, cost nothing. An empty stamina bar can no longer
lose you a fish you already hooked.

**Fish never escape**
Skipped entirely: the escape struggle that halves your reel speed and doubles the
stamina cost while a fish fights.

**Instant land**
The fish is in your inventory the moment it is hooked. No tug of war, no button hold.
Turn this off if you want to keep the fight and only keep the conveniences.
Fishing skill only rises while reeling, so with this on it stops leveling.

**Line never breaks**
Neither distance nor a fighting fish can snap the line. The float only goes away when
you are done with it.

## Config

Everything defaults to on. First launch writes the config file, and most settings take
effect on the next cast.

```
[1 - Bait]      One Bait Fits All, Bait Not Consumed
[2 - The Bite]  Fish Come To You, Auto Hook
[3 - The Fight] No Stamina Drain, Fish Never Escape, Instant Land
[4 - The Line]  Line Never Breaks
```

## Install

Install with r2modman (or any Thunderstore mod manager). BepInExPack is the only
dependency and a mod manager will pull it in for you.

## Build from source

```
./build.sh --no-bump --deploy
```

Rebuilds the current version, writes `dist/Fish_Easily-<version>.zip` and copies the DLL
into the r2modman profile. Import the zip in r2modman once first, so the
`<author>-Fish_Easily` folder exists. Restart Valheim to load the new build.
`./build.sh --help` lists the version bump options.

## Notes

- Built against the Valheim 1.0.16 game assemblies, with BepInEx 5.4.23.5 and HarmonyX.
- Private game members are reached through cached Harmony accessors, and every one is
  resolved at load. If a future Valheim patch renames something, the log says exactly
  which member went missing instead of quietly doing nothing.
- The plugin logs every method Harmony patched at startup. If a Valheim update removes a
  patch target, Harmony refuses to load the plugin and the log names the method.
- The log lines `Harmony reports 7 patched method(s)` and `Fish-Easily 1.0.0 loaded` in
  `BepInEx/LogOutput.log` mean it is really running.
- Multiplayer: Valheim runs fish behaviour on whichever player's game owns the fish,
  usually the first player who got near it. One Bait Fits All and Fish Come To You only
  work when that player has Fish-Easily, and when they do, they also apply to other
  players' floats nearby. Everything else follows the player holding the rod.

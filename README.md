# GLTPS1 — Co-op

A two-player co-op version of [GLTPS1](https://github.com/snavu/GLTPS1), the Girls' Last Tour fan game. One player is Chito, the other is Yuuri, and you travel the same world together.

![image](https://github.com/Rumodeus/GLTPS1/assets/91802844/07a0dbdc-b604-4f4a-856e-1d7ff2c442dc)

![image](https://github.com/Rumodeus/GLTPS1/assets/91802844/5eed27e9-5b4d-4311-a97c-553c23d0b183)

All credit for the game goes to its original developers. This fork only adds co-op and a few fixes.

## Playing

Download the zip from [Releases](../../releases), extract it and run `GLTPS1.exe`. Both players need the same release.

1. The host opens **CO-OP**, presses **host a game**, then **copy invite** and sends the invite to their friend.
2. The friend copies the invite, opens **CO-OP** and presses **join (paste invite)**.
3. The host starts the game. The host plays Chito, the friend plays Yuuri.

The friend can leave and rejoin; they come back where Yuuri was.

### Connection

The **connection** button on the co-op page picks how you connect:

- **auto** (default): a direct connection, opening the port on your router automatically (UDP 27015). If that doesn't work, it falls back to Epic's relay.
- **direct only**: never use Epic. The host may need to forward UDP 27015 by hand.
- **Epic only**: always go through Epic Online Services. Works behind any router.

No Epic Games account is needed.

### Saving

The game autosaves every few minutes and when you quit to the title. You can also save from the pause menu. There are three save slots; pick one when starting a new game or under **load** on the title screen. In co-op the host's save holds both girls.

## What co-op adds

- Both girls, the Kettenkrad (driver and back seat), the fuel barrel, the elevators and gunfire are synced.
- Pickups are shared: food goes to the supplies, and ammo goes to whoever has the gun.
- Kanazawa walks the same route on both screens, and both players see his dialogue.
- A girl who goes down can be revived by her partner.
- The window can be resized freely; the picture stretches to fit.

## Building

Built in Unity 2022.2.8f1, with the Mono scripting backend. Open the project and build for Windows from the `coop` branch.

Epic Online Services needs credentials that aren't in this repository. Without them, the game uses direct connection only. To use your own, copy `Tools/EosCredentials.example.cs` to `Assets/Assets/Scripts/Coop/EosCredentials.cs` and fill in your product's IDs from the Epic Developer Portal.

# PvzFmimic

A MelonLoader mod project for Plants vs. Zombies Fusion.

## Libraries

The repository uses the `libs-source` Git submodule, which points to the existing
SeedApocalypse4 repository containing the game's IL2CPP assemblies and .NET 6 /
MelonLoader reference libraries.

After cloning this repository, initialize the libraries:

```bash
git submodule update --init --recursive
```

## Build on Windows

Install the .NET 6 SDK, then run:

```bat
build.bat
```

The compiled mod is written to `bin/Release/net6.0/PvzFmimic.dll`.

GitHub Actions also builds the DLL on every push to `main` or `master`.

Arknights Resources
---

One-click Arknights resource download and extract utility.

- Automatically queries latest version
- Supports `cn`, `us`, `jp` servers (it's easy to add more)
- Bonus: decrypts `gamedata` _if you know the keys_ ([hint](https://github.com/djkaty/Il2CppInspector))
- Cross platform, probably?

## Usage

- Build it
- Copy some DLLs from `AssetStudioGUI`
- Create `config.json` (example below)
- Run it

```
{
  "DataRoot": "D:\\AknResourcesData",
  "Servers": [ "cn", "us", "jp" ],
  "ConvertAudio": true,
  "DecryptKeys": {
    "cn": [ null, null ],
    "us": [ null, null ],
    "jp": [ null, null ]
  }
}
```

## Dependencies

- [AssetStudio](https://github.com/Perfare/AssetStudio)
- .Net 5

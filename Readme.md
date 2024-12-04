# Labyrinthine - Tell Me Cosmetics
Display what cosmetics will be get in-game early.

## Download and Install
- Download and install MelonLoader (Stil use [Alpha-Dev build](https://nightly.link/LavaGang/MelonLoader/workflows/build/alpha-development)) manually.

- Download latest [RELEASE]() zip
- Extract and put folder `TellMeCosmetics` into the folder `Mods` in the game folder.

## Disclaimer
**NO MUCH TEST**.
Please use at own Risk.

## Known Issue
I'm not Unity developer but tried to searching solution
- ~~(Fixed) Sometimes Crash~~
- FrameDrop/Freezing sometime ( IDK how to search gameasset without performance drop )
- MelonLoader Make SteamUI Bug ( wait they update -_- )

## Feature Plan
- [x] **Display Case's Cosmetics item** - From ~~Cases board~~ Labyrinth Map
- [ ] **Show Item Name** - That really name not itemID
- [ ] **Show Image of Cosmetics Item**
- [ ] **Masked Locked Items** - For Non-OP mods
- [ ] **(Opt.) Animation** - Fade in / out?
- [ ] **(Opt.) Sound** - Reuse Game sound

- [ ] **(Cancelled) GetItem from Lobby** - Know Item by seeds without loading world 

## For Mod Developer
Here for detail if you wanna continue my job ;)

### Dev Requirement
- **ModLoader:** MelonLoader (Stil use [Alpha-Dev build](https://nightly.link/LavaGang/MelonLoader/workflows/build/alpha-development))
- **AssetSearch:** [UnityExplorer (Forked)](https://github.com/GrahamKracker/UnityExplorer) - Original not worked
- **RevEngCode:** [dnSpy](https://github.com/dnSpy/dnSpy)
- .Net SDK 6.0
 
### Classes/Method
#### [Intended to do] Know from Contract in "Lobby_PC" scene
`Il2CppRandomGeneration.Contracts.ContractUI` (UI)
`Il2CppRandomGeneration.Contracts.Contract` (data)
- Seed 
- SecondSeed
- ExcludedCustomizationItemIDs

*** Cannot Find Cosmetics selection logic yet. (Behavior - **Re-Enter** map will same spawned)

#### Cosmetic Item (Lobby)
`Il2CppCharacterCustomization.CustomizationItem`
- icon (Image)
- itemID (Reference ItemID)

### Cosmetic Item (In-game)
`Il2CppCharacterCustomization.CustomizationPickup`
- itemID (Reference ItemID)

### Optional - Save Check 
`Il2CppCharacterCustomization.CustomizationSave`
- IsItemUnlocked(Reference itemID)

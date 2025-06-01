# Labyrinthine - Tell Me Cosmetics
### Press `Tab` to display what cosmetics will get in-game.
#### Case: Discovered item
![mod example](https://github.com/limitbrk/Labyrinthine-TellMeCosmetics/blob/main/docs/Mod_Example1.png?raw=true)
#### Case: Discovered music item
![mod example](https://github.com/limitbrk/Labyrinthine-TellMeCosmetics/blob/main/docs/Mod_Example3_MusicDisc.png?raw=true)
#### Case: Undiscovered item when `RevealAllItems=false`
![mod example](https://github.com/limitbrk/Labyrinthine-TellMeCosmetics/blob/main/docs/Mod_Example2_Undiscovered.png?raw=true)
#### Case: When picked up
![mod example](https://github.com/limitbrk/Labyrinthine-TellMeCosmetics/blob/main/docs/Mod_Example4_Pickup_Item.png?raw=true)

## Mod Setting
The `Mods/TellMeCosmetics_config.cfg` file will be automatically created the first time you start the game.
```ini
[Gameplay]
RevealAllItems = false
```
- `RevealAllItems`: Show every item name, including undiscovered items. (Default = `false`)

## Mod Status
**ONLY Tested on Windows 11 AMDx64** 

## Features Plan
- [x] **Display Case's Cosmetics item** - From ~~Cases board~~ Labyrinth Map
- [x] **Show Item Name** - Display the actual item name instead of the item ID.
- [x] **Show Image of Cosmetics Item** - only item name don't know how it's look
- [X] **Masked Locked Items** - For Non-OP mods
- [X] **Mark UI when an item is picked up** -  to help users track their progress.
- ~~**(Cancelled) Tweak UI** - more readable~~ _(I'm lack Unity UI skill to made this)_
- ~~**(Cancelled) Animation** - Fade in / out?~~ 
- ~~**(Cancelled) GetItem from Lobby** - Know Item by seeds without loading world~~ _(Still looking for seeding machanics)_

# ChangeLog 
For the full changelog, check the [Github Release Notes.](https://github.com/limitbrk/Labyrinthine-TellMeCosmetics/releases)


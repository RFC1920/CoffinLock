# CoffinLock (Remod original)

[Download](https://code.remod.org/CoffinLock.cs)

Adds an associated code lock to a deployed coffin.  This feature can be enabled or disabled to facilitate the placement of coffins normally without the lock.  The lock code must be set and the lock locked to lock the coffin.

![](https://i.imgur.com/aupMhSp.jpg)

On first use, the player must enable first via the chat command /cl on.  This setting will remain until the player enters /cl off.

Coffinss are placed normally via the Rust user interface.

When the user attempts to pickup the lock, access will be denied.  However, they can pickup the coffin as they would normally, and the lock will also be removed.   A player cannot pickup a locked coffin.

There is currently no configuration required for CoffinLock.  Data files are used to record placed coffins and user enable status (one each).

## Permissions

- `cofflinlock.use` -- Allows player to placed a locked switch
- `cofflinlock.admin` --- Placeholder for future use

## Chat Commands

- `/cl` -- Display enable/disable status with instructions
- `/cl on` -- Enable locked coffin placement (coffin with an associated lock)
- `/cl off` -- Disable locked coffin placement (standard behavior without lock)
- `/cl who` -- Allows user with admin permission above to see who owns the coffin they are looking at

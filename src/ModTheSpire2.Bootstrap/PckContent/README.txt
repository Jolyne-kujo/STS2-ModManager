ModTheSpire 2 is a native StS2 mod package.
When StS2 loads this DLL, it pauses the native mod-loading loop and opens a
startup selector. The selector writes the chosen order and enabled state to the
game settings and replays the remaining mod loads in that order.

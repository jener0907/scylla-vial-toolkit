/*
 * Copyright 2026
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 2 of the License, or
 * (at your option) any later version.
 */

#pragma once

#define DYNAMIC_KEYMAP_LAYER_COUNT 4
#define DYNAMIC_KEYMAP_MACRO_COUNT 16

#define VIAL_KEYBOARD_UID \
    { 0x5B, 0x76, 0x3F, 0xFF, 0xA8, 0x70, 0x33, 0xC8 }

/* Left Esc and right Backspace: matrix (0,0) and (5,0). */
#define VIAL_UNLOCK_COMBO_ROWS \
    { 0, 5 }
#define VIAL_UNLOCK_COMBO_COLS \
    { 0, 0 }

#define VIAL_COMBO_ENTRIES 16
#define VIAL_TAP_DANCE_ENTRIES 16

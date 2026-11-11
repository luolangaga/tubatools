
--[[
-- Simple wrapper over gh_imgui lib for frame/window + begin/end functions.
--]]


_imgui_initialized = 0



-- Constants for gh_imgui.set_color()
--
IMGUI_WINDOW_BG_COLOR = 1
IMGUI_TITLE_BG_COLOR = 2
IMGUI_PLOTLINES_COLOR = 3
IMGUI_FRAME_BG_COLOR = 4
IMGUI_TITLE_BG_ACTIVE_COLOR = 5
IMGUI_TITLE_BG_COLLAPSED_COLOR = 6
IMGUI_PLOTHISTOGRAM_COLOR = 7
IMGUI_COMBO_BG_COLOR = 8
IMGUI_BUTTON_COLOR = 9
IMGUI_SEPARATOR_COLOR = 10
IMGUI_RESIZE_GRIP_COLOR = 11
IMGUI_PLOTLINE_HOVERED_COLOR = 12
IMGUI_PLOTHISTOGRAM_HOVERED_COLOR = 13
IMGUI_BUTTON_HOVERED_COLOR = 14
IMGUI_SEPARATOR_HOVERED_COLOR = 15
IMGUI_RESIZE_GRIP_HOVERED_COLOR = 16
IMGUI_HEADER_COLOR = 17
IMGUI_HEADER_HOVERED_COLOR = 18
IMGUI_SLIDER_GRAB_COLOR = 19
IMGUI_CHECK_MARK_COLOR = 20
IMGUI_SCROLLBAR_BG_COLOR = 21
IMGUI_SCROLLBAR_GRAB_COLOR = 22
IMGUI_SCROLLBAR_GRAB_HOVERED_COLOR = 23
IMGUI_TEXT_COLOR = 24
IMGUI_POPUP_BG_COLOR = 25
IMGUI_TEXT_DISABLED_COLOR = 26
IMGUI_CHILD_BG_COLOR = 27
IMGUI_BORDER_COLOR = 28
IMGUI_BORDER_SHADOW_COLOR = 29
IMGUI_FRAME_BG_HOVERED_COLOR = 30
IMGUI_FRAME_BG_ACTIVE_COLOR = 31
IMGUI_MENU_BAR_BG_COLOR = 32
IMGUI_SCROLLBAR_GRAB_ACTIVE_COLOR = 33
IMGUI_SLIDER_GRAB_ACTIVE_COLOR = 34
IMGUI_BUTTON_ACTIVE_COLOR = 35
IMGUI_HEADER_ACTIVE_COLOR = 36
IMGUI_SEPARATOR_ACTIVE_COLOR = 37
IMGUI_RESIZE_GRIP_ACTIVE_COLOR = 38
IMGUI_CLOSE_BUTTON_COLOR = 39
IMGUI_CLOSE_BUTTON_HOVERED_COLOR = 40 
IMGUI_CLOSE_BUTTON_ACTIVE_COLOR = 41
IMGUI_PLOTLINES_HOVERED_COLOR = 42
IMGUI_TEXT_SELECTED_BG_COLOR = 43
IMGUI_MODAL_WINDOW_DARKENING_COLOR = 44
IMGUI_DRAG_DROP_TARGET_COLOR = 45
IMGUI_NAV_HIGHLIGHT_COLOR = 46
IMGUI_NAV_WINDOW_IN_HIGHLIGHT_COLOR = 47
IMGUI_MODAL_WINDOW_DIM_BG_COLOR = 48
IMGUI_TAB_COLOR = 49
IMGUI_TAB_HOVERED_COLOR = 50
IMGUI_TAB_ACTIVE_COLOR = 51 -- deprecated in ImGui 1.90.9 - use IMGUI_TAB_SELECTED_COLOR
IMGUI_TAB_UNFOCUSED_COLOR = 52 -- deprecated in ImGui 1.90.9 - use IMGUI_TAB_DIMMED_COLOR
IMGUI_TAB_UNFOCUSED_ACTIVE_COLOR = 53 -- deprecated in ImGui 1.90.9 - use IMGUI_TAB_DIMMED_SELECTED_COLOR
IMGUI_TAB_SELECTED_COLOR = 51
IMGUI_TAB_DIMMED_COLOR = 52
IMGUI_TAB_DIMMED_SELECTED_COLOR = 53
IMGUI_TEXT_LINK_COLOR  = 54 -- ImGui 1.91.0
IMGUI_TABLE_HEADER_BG_COLOR = 55
IMGUI_TABLE_BORDER_STRONG_COLOR = 56
IMGUI_TABLE_BORDER_LIGHT_COLOR = 57
IMGUI_TABLE_ROW_BG_COLOR = 58
IMGUI_TABLE_ROW_BG_ALT_COLOR = 59
IMGUI_CHECK_BOX_SELECTED_BG_COLOR = 60



IMGUI_DRAWLIST_WINDOW = 0
IMGUI_DRAWLIST_BACKGROUND = 1
IMGUI_DRAWLIST_FOREGROUND = 2


IMGUI_WIDGET_SEPARATOR = 1
IMGUI_WIDGET_SAME_LINE = 2
IMGUI_WIDGET_BULLET = 3
IMGUI_WIDGET_VERTICAL_SPACING = 4



-- Window flags
ImGuiWindowFlags_None = 0
ImGuiWindowFlags_NoTitleBar = 1 -- Disable title-bar
ImGuiWindowFlags_NoResize = 2 -- Disable user resizing with the lower-right grip
ImGuiWindowFlags_NoMove = 4 -- Disable user moving the window
ImGuiWindowFlags_NoScrollbar = 8 -- Disable scrollbars (window can still scroll with mouse or programatically)
ImGuiWindowFlags_NoScrollWithMouse = 16 -- Disable user vertically scrolling with mouse wheel. On child window, mouse wheel will be forwarded to the parent unless NoScrollbar is also set.
ImGuiWindowFlags_NoCollapse = 32 -- Disable user collapsing window by double-clicking on it
ImGuiWindowFlags_AlwaysAutoResize = 64 -- Resize every window to its content every frame
ImGuiWindowFlags_NoBackground = 128 -- Disable drawing background color (WindowBg, etc.) and outside border. Similar as using SetNextWindowBgAlpha(0.0f).
ImGuiWindowFlags_NoSavedSettings = 256 -- Never load/save settings in .ini file
ImGuiWindowFlags_NoMouseInputs = 512 -- Disable catching mouse or keyboard inputs, hovering test with pass through.
ImGuiWindowFlags_MenuBar = 1024 -- Has a menu-bar
ImGuiWindowFlags_HorizontalScrollbar = 2048 -- Allow horizontal scrollbar to appear (off by default). You may use SetNextWindowContentSize(ImVec2(width,0.0f)); prior to calling Begin() to specify width. Read code in imgui_demo in the "Horizontal Scrolling" section.
ImGuiWindowFlags_NoFocusOnAppearing = 4096  -- Disable taking focus when transitioning from hidden to visible state
ImGuiWindowFlags_NoBringToFrontOnFocus = 8192 -- Disable bringing window to front when taking focus (e.g. clicking on it or programatically giving it focus)
ImGuiWindowFlags_AlwaysVerticalScrollbar = 16384 -- Always show vertical scrollbar (even if ContentSize.y < Size.y)
ImGuiWindowFlags_AlwaysHorizontalScrollbar = 32768 -- Always show horizontal scrollbar (even if ContentSize.x < Size.x)
--ImGuiWindowFlags_AlwaysUseWindowPadding = 65536 -- Removed in ImGui 1.90.9 - Ensure child windows without border uses style.WindowPadding (ignored by default for non-bordered child windows, because more convenient)
--ImGuiWindowFlags_ResizeFromAnySide = 131072 -- Removed in ImGui 1.90.9 - (WIP) Enable resize from any corners and borders. Your back-end needs to honor the different values of io.MouseCursor set by imgui.
ImGuiWindowFlags_NoNavInputs            = 65536 -- No gamepad/keyboard navigation within the window
ImGuiWindowFlags_NoNavFocus             = 131072 -- No focusing toward this window with gamepad/keyboard navigation (e.g. skipped by CTRL+TAB)
ImGuiWindowFlags_UnsavedDocument        = 262144 -- Append '*' to title without affecting the ID, as a convenience to avoid using the ### operator. When used in a tab/docking context, tab is selected on closure and closure is deferred by one frame to allow code to cancel the closure (with a confirmation popup, etc.) without flicker.
ImGuiWindowFlags_NoNav                  = ImGuiWindowFlags_NoNavInputs + ImGuiWindowFlags_NoNavFocus
ImGuiWindowFlags_NoDecoration           = ImGuiWindowFlags_NoTitleBar + ImGuiWindowFlags_NoResize + ImGuiWindowFlags_NoScrollbar + ImGuiWindowFlags_NoCollapse
ImGuiWindowFlags_NoInputs               = ImGuiWindowFlags_NoMouseInputs + ImGuiWindowFlags_NoNavInputs + ImGuiWindowFlags_NoNavFocus

   


-- Color edit flags
ImGuiColorEditFlags_None = 0
ImGuiColorEditFlags_NoAlpha = 2 -- ColorEdit, ColorPicker, ColorButton: ignore Alpha component (read 3 components from the input pointer).
ImGuiColorEditFlags_NoPicker = 4 -- ColorEdit: disable picker when clicking on colored square.
ImGuiColorEditFlags_NoOptions = 8 -- ColorEdit: disable toggling options menu when right-clicking on inputs/small preview.
ImGuiColorEditFlags_NoSmallPreview = 16-- ColorEdit, ColorPicker: disable colored square preview next to the inputs. (e.g. to show only the inputs)
ImGuiColorEditFlags_NoInputs = 32 -- ColorEdit, ColorPicker: disable inputs sliders/text widgets (e.g. to show only the small preview colored square).
ImGuiColorEditFlags_NoTooltip = 64 -- ColorEdit, ColorPicker, ColorButton: disable tooltip when hovering the preview.
ImGuiColorEditFlags_NoLabel = 128 -- ColorEdit, ColorPicker: disable display of inline text label (the label is still forwarded to the tooltip and picker).
ImGuiColorEditFlags_NoSidePreview = 256 -- ColorPicker: disable bigger color preview on right side of the picker, use small colored square preview instead.
ImGuiColorEditFlags_NoDragDrop = 512 -- ColorEdit: disable drag and drop target. ColorButton: disable drag and drop source.
ImGuiColorEditFlags_NoBorder = 1024 -- NEW in ImGui 1.90.9 -  ColorButton: disable border (which is enforced by default)
ImGuiColorEditFlags_NoColorMarkers  = 2048 --ColorEdit: disable rendering R/G/B/A color marker. May also be disabled globally by setting style.ColorMarkerSize = 0.

--// Alpha preview
--// - Prior to 1.91.8 (2025/01/21): alpha was made opaque in the preview by default using old name ImGuiColorEditFlags_AlphaPreview.
--// - We now display the preview as transparent by default. You can use ImGuiColorEditFlags_AlphaOpaque to use old behavior.
--// - The new flags may be combined better and allow finer controls.
ImGuiColorEditFlags_AlphaOpaque     = 4096  -- // ColorEdit, ColorPicker, ColorButton: disable alpha in the preview,. Contrary to _NoAlpha it may still be edited when calling ColorEdit4()/ColorPicker4(). For ColorButton() this does the same as _NoAlpha.
ImGuiColorEditFlags_AlphaNoBg       = 8192  -- // ColorEdit, ColorPicker, ColorButton: disable rendering a checkerboard background behind transparent color.
ImGuiColorEditFlags_AlphaPreviewHalf= 16384  -- // ColorEdit, ColorPicker, ColorButton: display half opaque / half transparent preview.

-- User Options (right-click on widget to change some of them). You can set application defaults using SetColorEditOptions(). The idea is that you probably don't want to override them in most of your calls, let the user choose and/or call SetColorEditOptions() during startup.
ImGuiColorEditFlags_AlphaBar = 262144 -- ColorEdit, ColorPicker: show vertical alpha bar/gradient in picker.
ImGuiColorEditFlags_HDR = 524288 --  (WIP) ColorEdit: Currently only disable 0.0f..1.0f limits in RGBA edition (note: you probably want to use ImGuiColorEditFlags_Float flag as well).
ImGuiColorEditFlags_RGB = 1048576 -- [Inputs] ColorEdit: choose one among RGB/HSV/HEX. ColorPicker: choose any combination using RGB/HSV/HEX.
ImGuiColorEditFlags_DisplayRGB = 1048576 -- [Inputs] ColorEdit: choose one among RGB/HSV/HEX. ColorPicker: choose any combination using RGB/HSV/HEX.
ImGuiColorEditFlags_HSV = 2097152 -- [Inputs]     
ImGuiColorEditFlags_HSV = 2097152 -- [Inputs]     
ImGuiColorEditFlags_DisplayHex = 4194304 -- [Inputs] 
ImGuiColorEditFlags_Uint8 = 8388608 -- [DataType]   // ColorEdit, ColorPicker, ColorButton: _display_ values formatted as 0..255. 
ImGuiColorEditFlags_Float = 16777216 --  [DataType]   // ColorEdit, ColorPicker, ColorButton: _display_ values formatted as 0.0f..1.0f floats instead of 0..255 integers. No round-trip of value via integers.
ImGuiColorEditFlags_PickerHueBar = 33554432 -- [Picker] // ColorPicker: bar for Hue, rectangle for Sat/Value.
ImGuiColorEditFlags_PickerHueWheel = 67108864 -- [Picker] // ColorPicker: wheel for Hue, triangle for Sat/Value.
ImGuiColorEditFlags_PickerNoRotate  = 134217728 -- [Picker] // ColorPicker: disable rotating Sat/Value triangle. Best set in io.ConfigColorEditFlags once.
ImGuiColorEditFlags_InputRGB = 268435456 -- [Input]      // ColorEdit, ColorPicker: input and output data in RGB format.
ImGuiColorEditFlags_InputHSV = 536870912 -- [Input]      // ColorEdit, ColorPicker: input and output data in HSV format.


-- Tree node flags
ImGuiTreeNodeFlags_None = 0 
ImGuiTreeNodeFlags_Selected = 1 -- Draw as selected
ImGuiTreeNodeFlags_Framed = 2 -- Full colored frame (e.g. for CollapsingHeader)
ImGuiTreeNodeFlags_AllowOverlap = 4  -- Hit testing to allow subsequent widgets to overlap this one
ImGuiTreeNodeFlags_NoTreePushOnOpen = 8 -- Don't do a TreePush() when open (e.g. for CollapsingHeader) = no extra indent nor pushing on ID stack
ImGuiTreeNodeFlags_NoAutoOpenOnLog = 16 -- Don't automatically and temporarily open node when Logging is active (by default logging will automatically open tree nodes)
ImGuiTreeNodeFlags_DefaultOpen = 32 -- Default node to be open
ImGuiTreeNodeFlags_OpenOnDoubleClick = 64 -- Need double-click to open node
ImGuiTreeNodeFlags_OpenOnArrow = 128 -- Only open when clicking on the arrow part. If ImGuiTreeNodeFlags_OpenOnDoubleClick is also set, single-click arrow or double-click all box to open.
ImGuiTreeNodeFlags_Leaf = 256 -- No collapsing, no arrow (use as a convenience for leaf nodes).
ImGuiTreeNodeFlags_Bullet = 512 -- Display a bullet instead of arrow
ImGuiTreeNodeFlags_FramePadding = 1024 -- Use FramePadding (even for an unframed text node) to vertically align text baseline to regular widget height. Equivalent to calling AlignTextToFramePadding().
ImGuiTreeNodeFlags_SpanAvailWidth       = 2048 -- Extend hit box to the right-most edge, even if not framed. This is not the default in order to allow adding other items on the same line. In the future we may refactor the hit system to be front-to-back, allowing natural overlaps and then this can become the default.
ImGuiTreeNodeFlags_SpanFullWidth        = 4096 -- Extend hit box to the left-most and right-most edges (bypass the indented area).
ImGuiTreeNodeFlags_SpanLabelWidth       = 8192 -- NEW in ImGui 1.90.9 - Narrow hit box + narrow hovering highlight, will only cover the label text.
ImGuiTreeNodeFlags_SpanAllColumns       = 16384 -- NEW in ImGui 1.90.9 - Frame will span all columns of its container table (text will still fit in current column)
ImGuiTreeNodeFlags_LabelSpanAllColumns  = 32768 -- Label will span all columns of its container table
ImGuiTreeNodeFlags_NavLeftJumpsToParent = 131072 -- Nav: left arrow moves back to parent. This is processed in TreePop() when there's an unfullfilled Left nav request remaining.
ImGuiTreeNodeFlags_CollapsingHeader     = ImGuiTreeNodeFlags_Framed + ImGuiTreeNodeFlags_NoTreePushOnOpen + ImGuiTreeNodeFlags_NoAutoOpenOnLog    
ImGuiTreeNodeFlags_DrawLinesNone        = 262144 -- No lines drawn
ImGuiTreeNodeFlags_DrawLinesFull        = 524288 -- Horizontal lines to child nodes. Vertical line drawn down to TreePop() position: cover full contents. Faster (for large trees).
ImGuiTreeNodeFlags_DrawLinesToNodes     = 1048576 --Horizontal lines to child nodes. Vertical line drawn down to bottom-most child node. Slower (for large trees).
   
--ImGuiTreeNodeFlags_NavLeftJumpsBackHere = 32768 -- (WIP) Nav: left direction may move to this TreeNode() from any of its child (items submitted between TreeNode and TreePop)
ImGuiTreeNodeFlags_NavLeftJumpsBackHere = ImGuiTreeNodeFlags_NavLeftJumpsToParent -- Renamed in 1.92
ImGuiTreeNodeFlags_SpanTextWidth        = ImGuiTreeNodeFlags_SpanLabelWidth      -- Renamed in 1.90.7
ImGuiTreeNodeFlags_AllowItemOverlap     = ImGuiTreeNodeFlags_AllowOverlap -- Renamed in 1.89.7




-- Input text flags:
ImGuiInputTextFlags_None                = 0
ImGuiInputTextFlags_CharsDecimal        = 1  --   // Allow 0123456789.+-*/
ImGuiInputTextFlags_CharsHexadecimal    = 2  --   // Allow 0123456789ABCDEFabcdef
ImGuiInputTextFlags_CharsScientific     = 4  --   // Allow 0123456789.+-*/eE (Scientific notation input)
ImGuiInputTextFlags_CharsUppercase      = 8  --   // Turn a..z into A..Z
ImGuiInputTextFlags_CharsNoBlank        = 16 --   // Filter out spaces, tabs

-- Inputs
ImGuiInputTextFlags_AllowTabInput       = 32 --   // Pressing TAB input a '\t' character into the text field
ImGuiInputTextFlags_EnterReturnsTrue    = 64 --   // Return 'true' when Enter is pressed (as opposed to every time the value was modified). Consider looking at the IsItemDeactivatedAfterEdit() function.
ImGuiInputTextFlags_EscapeClearsAll     = 128 --   // Escape key clears content if not empty, and deactivate otherwise (contrast to default behavior of Escape to revert)
ImGuiInputTextFlags_CtrlEnterForNewLine = 256 --   // In multi-line mode, validate with Enter, add new line with Ctrl+Enter (default is opposite: validate with Ctrl+Enter, add line with Enter).

-- Other options
ImGuiInputTextFlags_ReadOnly            = 512 --   // Read-only mode
ImGuiInputTextFlags_Password            = 1024 --  // Password mode, display all characters as '*', disable copy
ImGuiInputTextFlags_AlwaysOverwrite     = 2048 --  // Overwrite mode
ImGuiInputTextFlags_AutoSelectAll       = 4096 --  // Select entire text when first taking mouse focus
ImGuiInputTextFlags_ParseEmptyRefVal    = 8192 ---  // InputFloat(), InputInt(), InputScalar() etc. only: parse empty string as zero value.
ImGuiInputTextFlags_DisplayEmptyRefVal  = 16384 --  // InputFloat(), InputInt(), InputScalar() etc. only: when value is zero, do not display it. Generally used with ImGuiInputTextFlags_ParseEmptyRefVal.
ImGuiInputTextFlags_NoHorizontalScroll  = 32768 --  // Disable following the cursor horizontally
ImGuiInputTextFlags_NoUndoRedo          = 65536 --  // Disable undo/redo. Note that input text owns the text data while active, if you want to provide your own undo/redo stack you need e.g. to call ClearActiveID().

-- Elide display / Alignment
ImGuiInputTextFlags_ElideLeft			      = 131072 --	// When text doesn't fit, elide left side to ensure right side stays visible. Useful for path/filenames. Single-line only!

-- Callback features
ImGuiInputTextFlags_CallbackCompletion  = 262144 --  // Callback on pressing TAB (for completion handling)
ImGuiInputTextFlags_CallbackHistory     = 524288 -- // Callback on pressing Up/Down arrows (for history handling)
ImGuiInputTextFlags_CallbackAlways      = 1048576 -- // Callback on each iteration. User code may query cursor position, modify text buffer.
ImGuiInputTextFlags_CallbackCharFilter  = 2097152 -- // Callback on character inputs to replace or discard them. Modify 'EventChar' to replace or discard, or return 1 in callback to discard.
ImGuiInputTextFlags_CallbackResize      = 4194304 --  // Callback on buffer capacity changes request (beyond 'buf_size' parameter value), allowing the string to grow. Notify when the string wants to be resized (for string types which hold a cache of their Size). You will be provided a new BufSize in the callback and NEED to honor it. (see misc/cpp/imgui_stdlib.h for an example of using this)
ImGuiInputTextFlags_CallbackEdit        = 8388608 -- // Callback on any edit (note that InputText() already returns true on edit, the callback is useful mainly to manipulate the underlying buffer while focus is active)
-- Beta in v1.92.3
ImGuiInputTextFlags_WordWrap            = 16777216 -- // InputTextMultine(): word-wrap lines that are too long.





-- flags for gh_imgui.selectable()
ImGuiSelectableFlags_None = 0
ImGuiSelectableFlags_NoAutoClosePopups = 1 -- // Clicking this don't close parent popup window
ImGuiSelectableFlags_SpanAllColumns = 2 -- // Selectable frame can span all columns (text will still fit in current column)
ImGuiSelectableFlags_AllowDoubleClick = 4 -- // Generate press events on double clicks too
ImGuiSelectableFlags_Disabled = 8 -- // Cannot be selected, display greyed out text
ImGuiSelectableFlags_AllowOverlap   = 16 --  // (WIP) Hit testing to allow subsequent widgets to overlap this one
ImGuiSelectableFlags_Highlight          = 32 -- // Make the item be displayed as if it is hovered
ImGuiSelectableFlags_SelectOnNav        = 64 -- // Auto-select when moved into, unless Ctrl is held. Automatic when in a BeginMultiSelect() block.

ImGuiSelectableFlags_DontClosePopups    = ImGuiSelectableFlags_NoAutoClosePopups   -- Renamed in 1.91.0
ImGuiSelectableFlags_AllowItemOverlap   = ImGuiSelectableFlags_AllowOverlap        -- Renamed in 1.89.7




-- Tabbars
ImGuiTabBarFlags_None                           = 0
ImGuiTabBarFlags_Reorderable                    = 1 -- Allow manually dragging tabs to re-order them + New tabs are appended at the end of list
ImGuiTabBarFlags_AutoSelectNewTabs              = 2 -- Automatically select new tabs when they appear
ImGuiTabBarFlags_TabListPopupButton             = 4 --  Disable buttons to open the tab list popup
ImGuiTabBarFlags_NoCloseWithMiddleMouseButton   = 8  -- Disable behavior of closing tabs
ImGuiTabBarFlags_NoTabListScrollingButtons      = 16 -- Disable scrolling buttons (apply when fitting policy is ImGuiTabBarFlags_FittingPolicyScroll)
ImGuiTabBarFlags_NoTooltip                      = 32 -- Disable tooltips when hovering a tab
ImGuiTabBarFlags_DrawSelectedOverline           = 64 -- NEW ImGui 1.90.9 - Draw selected overline markers over selected tab
ImGuiTabBarFlags_FittingPolicyMixed             = 128 --   // Shrink down tabs when they don't fit, until width is style.TabMinWidthShrink, then enable scrolling buttons.
ImGuiTabBarFlags_FittingPolicyShrink            = 256 --   // Shrink down tabs when they don't fit
ImGuiTabBarFlags_FittingPolicyScroll            = 512 --   // Enable scrolling buttons when tabs don't fit
ImGuiTabBarFlags_FittingPolicyResizeDown        = ImGuiTabBarFlags_FittingPolicyShrink -- // Renamed in 1.92.2


-- tab item
ImGuiTabItemFlags_None                          = 0
ImGuiTabItemFlags_UnsavedDocument               = 1 -- Append '*' to title without affecting the ID, as a convenience to avoid using the ### operator. Also: tab is selected on closure and closure is deferred by one frame to allow code to undo it without flicker.
ImGuiTabItemFlags_SetSelected                   = 2 -- Trigger flag to programmatically make the tab selected when calling BeginTabItem()
ImGuiTabItemFlags_NoCloseWithMiddleMouseButton  = 4 -- Disable behavior of closing tabs (that are submitted with p_open != NULL) with middle mouse button. You can still repro this behavior on user's side with if (IsItemHovered() && IsMouseClicked(2)) *p_open = false.
ImGuiTabItemFlags_NoPushId                      = 8 -- Don't call PushID(tab->ID)/PopID() on BeginTabItem()/EndTabItem()
ImGuiTabItemFlags_NoTooltip                     = 16 --   // Disable tooltip for the given tab
ImGuiTabItemFlags_NoReorder                     = 32 --   // Disable reordering this tab or having another tab cross over this tab
ImGuiTabItemFlags_Leading                       = 64 --   // Enforce the tab position to the left of the tab bar (after the tab list popup button)
ImGuiTabItemFlags_Trailing                      = 128 --  // Enforce the tab position to the right of the tab bar (before the scrolling buttons)
ImGuiTabItemFlags_NoAssumedClosure              = 256 -- NEW ImGui 1.90.9 - // Tab is selected when trying to close + closure is not immediately assumed (will wait for user to stop submitting the tab). Otherwise closure is assumed when pressing the X, so if you keep submitting the tab may reappear at end of tab bar.


-- Hovered
ImGuiHoveredFlags_None                          = 0 -- Return true if directly over the item/window, not obstructed by another window, not obstructed by an active popup or modal blocking inputs under them.
ImGuiHoveredFlags_ChildWindows                  = 1 -- IsWindowHovered() only: Return true if any children of the window is hovered
ImGuiHoveredFlags_RootWindow                    = 2 -- IsWindowHovered() only: Test from root window (top most parent of the current hierarchy)
ImGuiHoveredFlags_AnyWindow                     = 4 -- IsWindowHovered() only: Return true if any window is hovered
ImGuiHoveredFlags_NoPopupHierarchy              = 8 -- IsWindowHovered() only: Do not consider popup hierarchy (do not treat popup emitter as parent of popup) (when used with _ChildWindows or _RootWindow)
ImGuiHoveredFlags_AllowWhenBlockedByPopup       = 32 --  // Return true even if a popup window is normally blocking access to this item/window
ImGuiHoveredFlags_AllowWhenBlockedByActiveItem  = 128 --  // Return true even if an active item is blocking access to this item/window. Useful for Drag and Drop patterns.
ImGuiHoveredFlags_AllowWhenOverlappedByItem     = 256 --   // IsItemHovered() only: Return true even if the item uses AllowOverlap mode and is overlapped by another hoverable item.
ImGuiHoveredFlags_AllowWhenOverlappedByWindow   = 512 --  // IsItemHovered() only: Return true even if the position is obstructed or overlapped by another window.
ImGuiHoveredFlags_AllowWhenDisabled             = 1024 --  // IsItemHovered() only: Return true even if the item is disabled
ImGuiHoveredFlags_NoNavOverride                 = 2048 --  // IsItemHovered() only: Disable using gamepad/keyboard navigation state when active, always query mouse

ImGuiHoveredFlags_AllowWhenOverlapped           = ImGuiHoveredFlags_AllowWhenOverlappedByItem + ImGuiHoveredFlags_AllowWhenOverlappedByWindow
ImGuiHoveredFlags_RectOnly                      = ImGuiHoveredFlags_AllowWhenBlockedByPopup + ImGuiHoveredFlags_AllowWhenBlockedByActiveItem + ImGuiHoveredFlags_AllowWhenOverlapped
ImGuiHoveredFlags_RootAndChildWindows           = ImGuiHoveredFlags_RootWindow + ImGuiHoveredFlags_ChildWindows

ImGuiHoveredFlags_ForTooltip                    = 4096 -- // Shortcut for standard flags when using IsItemHovered() + SetTooltip() sequence.

-- (Advanced) Mouse Hovering delays.
-- generally you can use ImGuiHoveredFlags_ForTooltip to use application-standardized flags.
-- use those if you need specific overrides.
ImGuiHoveredFlags_Stationary                    = 8192 -- // Require mouse to be stationary for style.HoverStationaryDelay (~0.15 sec) _at least one time_. After this, can move on same item/window. Using the stationary test tends to reduces the need for a long delay.
ImGuiHoveredFlags_DelayNone                     = 16384 -- // IsItemHovered() only: Return true immediately (default). As this is the default you generally ignore this.
ImGuiHoveredFlags_DelayShort                    = 32768 -- // IsItemHovered() only: Return true after style.HoverDelayShort elapsed (~0.15 sec) (shared between items) + requires mouse to be stationary for style.HoverStationaryDelay (once per item).
ImGuiHoveredFlags_DelayNormal                   = 65536 -- // IsItemHovered() only: Return true after style.HoverDelayNormal elapsed (~0.40 sec) (shared between items) + requires mouse to be stationary for style.HoverStationaryDelay (once per item).
ImGuiHoveredFlags_NoSharedDelay                 = 131072 -- // IsItemHovered() only: Disable shared delay system where moving from one item to the next keeps the previous timer for a short time (standard for tooltips with long delays)



-- Flags for slider_1i_v2() and vslider_1i_v2().
ImGuiSliderFlags_None                   = 0
--ImGuiSliderFlags_AlwaysClamp          = 16 -- Clamp value to min/max bounds when input manually with CTRL+Click. By default CTRL+Click allows going out of bounds.
ImGuiSliderFlags_Logarithmic            = 32 -- Make the widget logarithmic (linear otherwise). Consider using ImGuiSliderFlags_NoRoundToFormat with this if using a format-string with small amount of digits.
ImGuiSliderFlags_NoRoundToFormat        = 64 -- Disable rounding underlying value to match precision of the display format string (e.g. %.3f values are rounded to those 3 digits).
ImGuiSliderFlags_NoInput                = 128 -- Disable CTRL+Click or Enter key allowing to input text directly into the widget.
ImGuiSliderFlags_WrapAround             = 256 -- Enable wrapping around from max to min and from min to max (only supported by DragXXX() functions for now.
ImGuiSliderFlags_ClampOnInput           = 512 -- Clamp value to min/max bounds when input manually with CTRL+Click. By default CTRL+Click allows going out of bounds.
ImGuiSliderFlags_ClampZeroRange         = 1024 -- Clamp even if min==max==0.0f. Otherwise due to legacy reason DragXXX functions don't clamp with those values. When your clamping limits are dynamic you almost always want to use it.
ImGuiSliderFlags_NoSpeedTweaks          = 2048 -- Disable keyboard modifiers altering tweak speed. Useful if you want to alter tweak speed yourself based on your own logic.
ImGuiSliderFlags_ColorMarkers           = 4096 -- DragScalarN(), SliderScalarN(): Draw R/G/B/A color markers on each component.
ImGuiSliderFlags_AlwaysClamp            = ImGuiSliderFlags_ClampOnInput + ImGuiSliderFlags_ClampZeroRange

-- Flags for gh_imgui.invisible_button() function.
ImGuiButtonFlags_None                   = 0
ImGuiButtonFlags_MouseButtonLeft        = 1 --// React on left mouse button (default)
ImGuiButtonFlags_MouseButtonRight       = 2 --// React on right mouse button
ImGuiButtonFlags_MouseButtonMiddle      = 4 --// React on center mouse button
ImGuiButtonFlags_EnableNav              = 8 --// InvisibleButton(): do not disable navigation/tabbing. Otherwise disabled by default.
ImGuiButtonFlags_AllowOverlap           = 4096 --// Hit testing will allow subsequent widgets to overlap this one. Require previous frame HoveredId to match before being usable. Shortcut to calling SetNextItemAllowOverlap().



-- Flags for gh_imgui.file_browser_init() function.
ImGuiFileBrowserFlags_Default           = 0 
ImGuiFileBrowserFlags_SelectDirectory   = 1 --// select directory instead of regular file
ImGuiFileBrowserFlags_EnterNewFilename  = 2 --// allow user to enter new filename when selecting regular file
ImGuiFileBrowserFlags_NoModal           = 4 --// file browsing window is modal by default. specify this to use a popup window
ImGuiFileBrowserFlags_NoTitleBar        = 8 --// hide window title bar
ImGuiFileBrowserFlags_NoStatusBar       = 16 --// hide status bar at the bottom of browsing window
ImGuiFileBrowserFlags_CloseOnEsc        = 32 --// close file browser when pressing 'ESC'
ImGuiFileBrowserFlags_CreateNewDir      = 64 --// allow user to create new directory
ImGuiFileBrowserFlags_MultipleSelection = 128 --// allow user to select multiple files. this will hide ImGuiFileBrowserFlags_EnterNewFilename
ImGuiFileBrowserFlags_HideRegularFiles  = 256 --// hide regular files when ImGuiFileBrowserFlags_SelectDirectory is enabled
ImGuiFileBrowserFlags_ConfirmOnEnter    = 512 --// confirm selection when pressing 'ENTER'
ImGuiFileBrowserFlags_SkipItemsCausingError = 1024 --// when entering a new directory, any error will interrupt the process, causing the file browser to fall back to the working directory.
                                                   --// with this flag, if an error is caused by a specific item in the directory, that item will be skipped, allowing the process to continue.
ImGuiFileBrowserFlags_EditPathString        = 2048 --// allow user to directly edit the whole path string


-- Flages for gh_imgui.begin_child_v2() function.
ImGuiChildFlags_None                    = 0
ImGuiChildFlags_Border                  = 1 --  // Show an outer border and enable WindowPadding. (Important: this is always == 1 == true for legacy reason)
ImGuiChildFlags_AlwaysUseWindowPadding  = 2 --  // Pad with style.WindowPadding even if no border are drawn (no padding by default for non-bordered child windows because it makes more sense)
ImGuiChildFlags_ResizeX                 = 4 --  // Allow resize from right border (layout direction). Enable .ini saving (unless ImGuiWindowFlags_NoSavedSettings passed to window flags)
ImGuiChildFlags_ResizeY                 = 8 --  // Allow resize from bottom border (layout direction). "
ImGuiChildFlags_AutoResizeX             = 16 -- // Enable auto-resizing width. Read "IMPORTANT: Size measurement" details above.
ImGuiChildFlags_AutoResizeY             = 32 -- // Enable auto-resizing height. Read "IMPORTANT: Size measurement" details above.
ImGuiChildFlags_AlwaysAutoResize        = 64 -- // Combined with AutoResizeX/AutoResizeY. Always measure size even when child is hidden, always return true, always disable clipping optimization! NOT RECOMMENDED.
ImGuiChildFlags_FrameStyle              = 128 -- // Style the child window like a framed item: use FrameBg, FrameRounding, FrameBorderSize, FramePadding instead of ChildBg, ChildRounding, ChildBorderSize, WindowPadding.
ImGuiChildFlags_NavFlattened            = 256 -- // Dear ImGui 1.90.9 - Share focus scope, allow gamepad/keyboard navigation to cross over parent border to this child or between sibling child windows.





-------------------------------------------------------------
-- Flags for new table API added with ImGui 1.80.
-------------------------------------------------------------

-- Flags for gh_imgui.begin_table()
--
-- Features
ImGuiTableFlags_None                       = 0
ImGuiTableFlags_Resizable                  = 1 --// Enable resizing columns.
ImGuiTableFlags_Reorderable                = 2 --// Enable reordering columns in header row (need calling TableSetupColumn() + TableHeadersRow() to display headers)
ImGuiTableFlags_Hideable                   = 4 --// Enable hiding/disabling columns in context menu.
ImGuiTableFlags_Sortable                   = 8 --// Enable sorting. Call TableGetSortSpecs() to obtain sort specs. Also see ImGuiTableFlags_SortMulti and ImGuiTableFlags_SortTristate.
ImGuiTableFlags_NoSavedSettings            = 16 --// Disable persisting columns order, width and sort settings in the .ini file.
ImGuiTableFlags_ContextMenuInBody          = 32 -- // Right-click on columns body/contents will display table context menu. By default it is available in TableHeadersRow().
-- Decorations
ImGuiTableFlags_RowBg                      = 64 --// Set each RowBg color with ImGuiCol_TableRowBg or ImGuiCol_TableRowBgAlt (equivalent of calling TableSetBgColor with ImGuiTableBgFlags_RowBg0 on each row manually)
ImGuiTableFlags_BordersInnerH              = 128 --// Draw horizontal borders between rows.
ImGuiTableFlags_BordersOuterH              = 256 --// Draw horizontal borders at the top and bottom.
ImGuiTableFlags_BordersInnerV              = 512 --// Draw vertical borders between columns.
ImGuiTableFlags_BordersOuterV              = 1024 --// Draw vertical borders on the left and right sides.
ImGuiTableFlags_BordersH                   = ImGuiTableFlags_BordersInnerH + ImGuiTableFlags_BordersOuterH -- // Draw horizontal borders.
ImGuiTableFlags_BordersV                   = ImGuiTableFlags_BordersInnerV + ImGuiTableFlags_BordersOuterV -- // Draw vertical borders.
ImGuiTableFlags_BordersInner               = ImGuiTableFlags_BordersInnerV + ImGuiTableFlags_BordersInnerH --// Draw inner borders.
ImGuiTableFlags_BordersOuter               = ImGuiTableFlags_BordersOuterV + ImGuiTableFlags_BordersOuterH --// Draw outer borders.
ImGuiTableFlags_Borders                    = ImGuiTableFlags_BordersInner + ImGuiTableFlags_BordersOuter --// Draw all borders.
ImGuiTableFlags_NoBordersInBody            = 2048 --// [ALPHA] Disable vertical borders in columns Body (borders will always appears in Headers). -> May move to style
ImGuiTableFlags_NoBordersInBodyUntilResize = 4096 --// [ALPHA] Disable vertical borders in columns Body until hovered for resize (borders will always appears in Headers). -> May move to style
-- Sizing Policy (read above for defaults)
ImGuiTableFlags_SizingFixedFit             = 8192 --// Columns default to _WidthFixed or _WidthAuto (if resizable or not resizable), matching contents width.
ImGuiTableFlags_SizingFixedSame            = 16384 --// Columns default to _WidthFixed or _WidthAuto (if resizable or not resizable), matching the maximum contents width of all columns. Implicitly enable ImGuiTableFlags_NoKeepColumnsVisible.
ImGuiTableFlags_SizingStretchProp          = 24576 --// Columns default to _WidthStretch with default weights proportional to each columns contents widths.
ImGuiTableFlags_SizingStretchSame          = 32768 --// Columns default to _WidthStretch with default weights all equal, unless overriden by TableSetupColumn().
-- Sizing Extra Options
ImGuiTableFlags_NoHostExtendX              = 65536 --// Make outer width auto-fit to columns, overriding outer_size.x value. Only available when ScrollX/ScrollY are disabled and Stretch columns are not used.
ImGuiTableFlags_NoHostExtendY              = 131072 --// Make outer height stop exactly at outer_size.y (prevent auto-extending table past the limit). Only available when ScrollX/ScrollY are disabled. Data below the limit will be clipped and not visible.
ImGuiTableFlags_NoKeepColumnsVisible       = 262144 --// Disable keeping column always minimally visible when ScrollX is off and table gets too small. Not recommended if columns are resizable.
ImGuiTableFlags_PreciseWidths              = 524288 --// Disable distributing remainder width to stretched columns (width allocation on a 100-wide table with 3 columns: Without this flag: 33,33,34. With this flag: 33,33,33). With larger number of columns, resizing will appear to be less smooth.
-- Clipping
ImGuiTableFlags_NoClip                     = 1048576 --// Disable clipping rectangle for every individual columns (reduce draw command count, items will be able to overflow into other columns). Generally incompatible with TableSetupScrollFreeze().
-- Padding
ImGuiTableFlags_PadOuterX                  = 2097152 --// Default if BordersOuterV is on. Enable outer-most padding. Generally desirable if you have headers.
ImGuiTableFlags_NoPadOuterX                = 4194304 --// Default if BordersOuterV is off. Disable outer-most padding.
ImGuiTableFlags_NoPadInnerX                = 8388608 --// Disable inner padding between columns (double inner padding if BordersOuterV is on, single inner padding if BordersOuterV is off).
-- Scrolling
ImGuiTableFlags_ScrollX                    = 16777216 --// Enable horizontal scrolling. Require 'outer_size' parameter of BeginTable() to specify the container size. Changes default sizing policy. Because this create a child window, ScrollY is currently generally recommended when using ScrollX.
ImGuiTableFlags_ScrollY                    = 33554432 --// Enable vertical scrolling. Require 'outer_size' parameter of BeginTable() to specify the container size.
-- Sorting
ImGuiTableFlags_SortMulti                  = 67108864 --// Hold shift when clicking headers to sort on multiple column. TableGetSortSpecs() may return specs where (SpecsCount > 1).
ImGuiTableFlags_SortTristate               = 134217728 --// Allow no sorting, disable default sorting. TableGetSortSpecs() may return specs where (SpecsCount == 0).
-- Miscellaneous
ImGuiTableFlags_HighlightHoveredColumn     = 268435456 -- // Highlight column headers when hovered (may evolve into a fuller highlight)


-- Flags for gh_imgui.table_setup_column()
--
ImGuiTableColumnFlags_None                  = 0
ImGuiTableColumnFlags_Disabled              = 1 --   // Overriding/master disable flag: hide column, won't show in context menu (unlike calling TableSetColumnEnabled() which manipulates the user accessible state)
ImGuiTableColumnFlags_DefaultHide           = 2 --   // Default as a hidden/disabled column.
ImGuiTableColumnFlags_DefaultSort           = 4 --   // Default as a sorting column.
ImGuiTableColumnFlags_WidthStretch          = 8 --   // Column will stretch. Preferable with horizontal scrolling disabled (default if table sizing policy is _SizingStretchSame or _SizingStretchProp).
ImGuiTableColumnFlags_WidthFixed            = 16 --   // Column will not stretch. Preferable with horizontal scrolling enabled (default if table sizing policy is _SizingFixedFit and table is resizable).
ImGuiTableColumnFlags_NoResize              = 32 --   // Disable manual resizing.
ImGuiTableColumnFlags_NoReorder             = 64 --   // Disable manual reordering this column, this will also prevent other columns from crossing over this column.
ImGuiTableColumnFlags_NoHide                = 128 --   // Disable ability to hide/disable this column.
ImGuiTableColumnFlags_NoClip                = 256 --   // Disable clipping for this column (all NoClip columns will render in a same draw command).
ImGuiTableColumnFlags_NoSort                = 512 --  // Disable ability to sort on this field (even if ImGuiTableFlags_Sortable is set on the table).
ImGuiTableColumnFlags_NoSortAscending       = 1024 -- // Disable ability to sort in the ascending direction.
ImGuiTableColumnFlags_NoSortDescending      = 2048 --  // Disable ability to sort in the descending direction.
ImGuiTableColumnFlags_NoHeaderLabel         = 4096 --  // TableHeadersRow() will not submit label for this column. Convenient for some small columns. Name will still appear in context menu.
ImGuiTableColumnFlags_NoHeaderWidth         = 8192 --  // Disable header text width contribution to automatic column width.
ImGuiTableColumnFlags_PreferSortAscending   = 16384 --  // Make the initial sort direction Ascending when first sorting on this column (default).
ImGuiTableColumnFlags_PreferSortDescending  = 32768 --  // Make the initial sort direction Descending when first sorting on this column.
ImGuiTableColumnFlags_IndentEnable          = 65536 --  // Use current Indent value when entering cell (default for column 0).
ImGuiTableColumnFlags_IndentDisable         = 131072 -- // Ignore current Indent value when entering cell (default for columns > 0). Indentation changes _within_ the cell will still be honored.
ImGuiTableColumnFlags_AngledHeader          = 262144 -- // TableHeadersRow() will submit an angled header row for this column. Note this will add an extra row.

-- Output status flags, read-only via gh_imgui.table_get_column_flags()
--
ImGuiTableColumnFlags_IsEnabled             = 16777216 --  // Status: is enabled == not hidden by user/api (referred to as "Hide" in _DefaultHide and _NoHide) flags.
ImGuiTableColumnFlags_IsVisible             = 33554432 --  // Status: is visible == is enabled AND not clipped by scrolling.
ImGuiTableColumnFlags_IsSorted              = 67108864 --  // Status: is currently part of the sort specs
ImGuiTableColumnFlags_IsHovered             = 134217728 -- // Status: is hovered by mouse



-- Flags for gh_imgui.table_next_row()
--
ImGuiTableRowFlags_None                         = 0
ImGuiTableRowFlags_Headers                      = 1 --// Identify header row (set default background color + width of its contents accounted different for auto column width)

-- Enum for gh_imgui::table_set_bg_color()
--
ImGuiTableBgTarget_None                         = 0
ImGuiTableBgTarget_RowBg0                       = 1 --// Set row background color 0 (generally used for background, automatically set when ImGuiTableFlags_RowBg is used)
ImGuiTableBgTarget_RowBg1                       = 2 --// Set row background color 1 (generally used for selection marking)
ImGuiTableBgTarget_CellBg                       = 3 --// Set cell background color (top-most color)



-- Enum for gh_imgui::table_set_bg_color()
ImGuiStyleVar_Alpha = 0 --                     // float     Alpha
ImGuiStyleVar_DisabledAlpha = 1 --             // float     DisabledAlpha
ImGuiStyleVar_WindowPadding = 2 --             // ImVec2    WindowPadding
ImGuiStyleVar_WindowRounding = 3 --            // float     WindowRounding
ImGuiStyleVar_WindowBorderSize = 4 --          // float     WindowBorderSize
ImGuiStyleVar_WindowMinSize = 5 --             // ImVec2    WindowMinSize
ImGuiStyleVar_WindowTitleAlign = 6 --          // ImVec2    WindowTitleAlign
ImGuiStyleVar_ChildRounding = 7 --             // float     ChildRounding
ImGuiStyleVar_ChildBorderSize = 8 --           // float     ChildBorderSize
ImGuiStyleVar_PopupRounding = 9 --             // float     PopupRounding
ImGuiStyleVar_PopupBorderSize = 10 --          // float     PopupBorderSize
ImGuiStyleVar_FramePadding = 11 --             // ImVec2    FramePadding
ImGuiStyleVar_FrameRounding = 12 --            // float     FrameRounding
ImGuiStyleVar_FrameBorderSize = 13 --          // float     FrameBorderSize
ImGuiStyleVar_ItemSpacing = 14 --              // ImVec2    ItemSpacing
ImGuiStyleVar_ItemInnerSpacing = 15 --         // ImVec2    ItemInnerSpacing
ImGuiStyleVar_IndentSpacing = 16 --            // float     IndentSpacing
ImGuiStyleVar_CellPadding = 17 --              // ImVec2    CellPadding
ImGuiStyleVar_ScrollbarSize = 18 --            // float     ScrollbarSize
ImGuiStyleVar_ScrollbarRounding = 19 --        // float     ScrollbarRounding
ImGuiStyleVar_ScrollbarPadding = 20 --        // float     ScrollbarPadding
ImGuiStyleVar_GrabMinSize = 21 --              // float     GrabMinSize
ImGuiStyleVar_GrabRounding = 22 --             // float     GrabRounding
ImGuiStyleVar_ImageRounding = 23 --            // float     ImageRounding
ImGuiStyleVar_ImageBorderSize = 24 --          // float     ImageBorderSize
ImGuiStyleVar_TabRounding = 25 --              // float     TabRounding
ImGuiStyleVar_TabBorderSize = 26 --            // float     TabBorderSize
ImGuiStyleVar_TabMinWidthBase = 27 --          // float     TabMinWidthBase
ImGuiStyleVar_TabMinWidthShrink = 28 --        // float     TabMinWidthShrink
ImGuiStyleVar_TabBarBorderSize = 29 --         // float     TabBarBorderSize
ImGuiStyleVar_TabBarOverlineSize = 30 --       // float     TabBarOverlineSize
ImGuiStyleVar_TableAngledHeadersAngle = 31 --  // float     TableAngledHeadersAngle
ImGuiStyleVar_TableAngledHeadersTextAlign = 32 --// ImVec2  TableAngledHeadersTextAlign
ImGuiStyleVar_TreeLinesSize = 33 --            // float     TreeLinesSize
ImGuiStyleVar_TreeLinesRounding = 34 --        // float     TreeLinesRounding
ImGuiStyleVar_MenuItemRounding = 35 --         // float     MenuItemRounding
ImGuiStyleVar_SelectableRounding = 36 --       // float     SelectableRounding
ImGuiStyleVar_DragDropTargetRounding = 37 --   // float     DragDropTargetRounding
ImGuiStyleVar_ButtonTextAlign = 38 --          // ImVec2    ButtonTextAlign
ImGuiStyleVar_SelectableTextAlign = 39 --      // ImVec2    SelectableTextAlign
ImGuiStyleVar_SeparatorSize = 40 --            // float     SeparatorSize
ImGuiStyleVar_SeparatorTextBorderSize = 41 --  // float     SeparatorTextBorderSize
ImGuiStyleVar_SeparatorTextAlign = 42 --       // ImVec2    SeparatorTextAlign
ImGuiStyleVar_SeparatorTextPadding = 43 --     // ImVec2    SeparatorTextPadding


-----------------------------------------------------------------------------
-- Constants for ImNodes

-- Enums for gh_imgui.imnodes_begin_input_attribute() and gh_imgui.imnodes_begin_output_attribute()
-- 
ImNodesPinShape_Circle = 0
ImNodesPinShape_CircleFilled = 1
ImNodesPinShape_Triangle = 2
ImNodesPinShape_TriangleFilled = 3
ImNodesPinShape_Quad = 4
ImNodesPinShape_QuadFilled = 5

-- Enums for gh_imgui.imnodes_push_color_style()
--
ImNodesCol_NodeBackground = 0
ImNodesCol_NodeBackgroundHovered = 1
ImNodesCol_NodeBackgroundSelected = 2
ImNodesCol_NodeOutline = 3
ImNodesCol_TitleBar = 4
ImNodesCol_TitleBarHovered = 5
ImNodesCol_TitleBarSelected = 6
ImNodesCol_Link = 7
ImNodesCol_LinkHovered = 8
ImNodesCol_LinkSelected = 9
ImNodesCol_Pin = 10
ImNodesCol_PinHovered = 11
ImNodesCol_BoxSelector = 12
ImNodesCol_BoxSelectorOutline = 13
ImNodesCol_GridBackground = 14
ImNodesCol_GridLine = 15
ImNodesCol_MiniMapBackground = 16
ImNodesCol_MiniMapBackgroundHovered = 17
ImNodesCol_MiniMapOutline = 18
ImNodesCol_MiniMapOutlineHovered = 19
ImNodesCol_MiniMapNodeBackground = 20
ImNodesCol_MiniMapNodeBackgroundHovered = 21
ImNodesCol_MiniMapNodeBackgroundSelected = 22
ImNodesCol_MiniMapNodeOutline = 23
ImNodesCol_MiniMapLink = 24
ImNodesCol_MiniMapLinkSelected = 25

-- Enums for gh_imgui.imnodes_push_style_var()
--
ImNodesStyleVar_GridSpacing = 0
ImNodesStyleVar_NodeCornerRounding = 1
ImNodesStyleVar_NodePaddingHorizontal = 2
ImNodesStyleVar_NodePaddingVertical = 3
ImNodesStyleVar_NodeBorderThickness = 4
ImNodesStyleVar_LinkThickness = 5
ImNodesStyleVar_LinkLineSegmentsPerLength = 6
ImNodesStyleVar_LinkHoverDistance = 7
ImNodesStyleVar_PinCircleRadius = 8
ImNodesStyleVar_PinQuadSideLength = 9
ImNodesStyleVar_PinTriangleSideLength = 10
ImNodesStyleVar_PinLineThickness = 11
ImNodesStyleVar_PinHoverRadius = 12
ImNodesStyleVar_PinOffset = 13

-- Enums for gh_imgui.imnodes_minimap()
--
ImNodesMiniMapLocation_BottomLeft = 0
ImNodesMiniMapLocation_BottomRight = 1
ImNodesMiniMapLocation_TopLeft = 2
ImNodesMiniMapLocation_TopRight = 3




-----------------------------------------------------------------------------
-- Constants for knobs
--
ImGuiKnobVariant_Tick = 1
ImGuiKnobVariant_Dot = 2
ImGuiKnobVariant_Wiper = 4
ImGuiKnobVariant_WiperOnly = 8
ImGuiKnobVariant_WiperDot = 16
ImGuiKnobVariant_Stepped = 32
ImGuiKnobVariant_Space = 64 

ImGuiKnobFlags_NoTitle = 1
ImGuiKnobFlags_NoInput = 2
ImGuiKnobFlags_ValueTooltip = 4
ImGuiKnobFlags_DragHorizontal = 8
ImGuiKnobFlags_DragVertical = 16
ImGuiKnobFlags_Logarithmic = 32
ImGuiKnobFlags_AlwaysClamp = 64
ImGuiKnobFlags_ReadOnlyInput = 1024 -- this is special flag fopr GeeXLab only.
ImGuiKnobFlags_DisableMouse = 2048 -- this is special flag fopr GeeXLab only.



-----------------------------------------------------------------------------
-- Constants for toggles
--
ImGuiToggleFlags_None                   = 0
ImGuiToggleFlags_Animated               = 1   -- The toggle's knob should be animated.
ImGuiToggleFlags_BorderedFrame          = 8   -- // The toggle should have a border drawn on the frame.
ImGuiToggleFlags_BorderedKnob           = 16  -- // The toggle should have a border drawn on the knob.
ImGuiToggleFlags_ShadowedFrame          = 32  -- // The toggle should have a shadow drawn under the frame.
ImGuiToggleFlags_ShadowedKnob           = 64  -- // The toggle should have a shadow drawn under the knob.
ImGuiToggleFlags_A11y                   = 256 -- // The toggle should draw on and off glyphs to help indicate its state.



-----------------------------------------------------------------------------
-- Constants for ImPlot
--
ImPlotFlags_None = 0   --    // default
ImPlotFlags_NoTitle = 1 -- // the plot title will not be displayed (titles are also hidden if preceded by double hashes, e.g. "##MyPlot")
ImPlotFlags_NoLegend = 2 -- // the legend will not be displayed
ImPlotFlags_NoMouseText = 4 --  // the mouse position, in plot coordinates, will not be displayed inside of the plot
ImPlotFlags_NoInputs = 8 -- // the user will not be able to interact with the plot
ImPlotFlags_NoMenus = 16 --  // the user will not be able to open context menus
ImPlotFlags_NoBoxSelect = 32 --  // the user will not be able to box-select
ImPlotFlags_NoFrame = 64 --  // the ImGui frame will not be rendered
ImPlotFlags_Equal = 128 --  // x and y axes pairs will be constrained to have the same units/pixel
ImPlotFlags_Crosshairs = 256 -- // the default mouse cursor will be replaced with a crosshair when hovered
ImPlotFlags_CanvasOnly = ImPlotFlags_NoTitle + ImPlotFlags_NoLegend + ImPlotFlags_NoMenus + ImPlotFlags_NoBoxSelect + ImPlotFlags_NoMouseText





-----------------------------------------------------------------------------
--
function imgui_init(style)

  gh_imgui.init()
  _imgui_initialized = 1

  -- Possible styles: 
  -- "classic"
  -- "dark"
  -- "light"
  --
  gh_imgui.set_style_colors(style)
end


function imgui_init_v2(init_filename)
  gh_imgui.init_v2(init_filename)
  _imgui_initialized = 1
end



-----------------------------------------------------------------------------
--
function imgui_frame_begin_v2(mouse_x, mouse_y)

  if (_imgui_initialized == 0) then
    gh_imgui.init()
    _imgui_initialized = 1
  end

  local win_w, win_h = gh_window.getsize(0)

  local LEFT_BUTTON = 1
  local mouse_left_button = gh_input.mouse_get_button_state(LEFT_BUTTON) 
  local RIGHT_BUTTON = 2
  local mouse_right_button = gh_input.mouse_get_button_state(RIGHT_BUTTON) 


  local mouse_wheel = 0

  local mouse_wheel_delta = gh_input.mouse_get_wheel_delta()
  if (mouse_wheel_delta ~= 0) then
    if (mouse_wheel_delta > 0) then
      mouse_wheel = mouse_wheel + 1
    elseif (mouse_wheel_delta < 0) then
      mouse_wheel = mouse_wheel - 1
    end  
  end
  gh_input.mouse_reset_wheel_delta()


  local dt = gh_utils.get_time_step()
  gh_imgui.frame_begin_v2(win_w, win_h, mouse_x, mouse_y, mouse_left_button, mouse_right_button, mouse_wheel, dt)

  --gh_imgui.frame_begin(win_w, win_h, mouse_x, mouse_y, mouse_left_button, mouse_right_button)
end  


-----------------------------------------------------------------------------
--
function imgui_frame_begin()

  local mouse_x, mouse_y = gh_input.mouse_get_position()
  imgui_frame_begin_v2(mouse_x, mouse_y)

end  


-----------------------------------------------------------------------------
--
function imgui_frame_end()
  gh_imgui.frame_end()
end



-----------------------------------------------------------------------------
--
function imgui_window_begin_v1(label, width, height, posx, posy)

  -- Flags for window style, window position and window size.
  --
  local pos_size_flag_always = 1 -- Always set the pos and/or size
  local pos_size_flag_once = 2 -- Set the pos and/or size once per runtime session (only the first call with succeed)
  local pos_size_flag_first_use_ever = 4  -- Set the pos and/or size if the window has no saved data (if doesn't exist in the .ini file)
  local pos_size_flag_appearing = 8  -- Set the pos and/or size if the window is appearing after being hidden/inactive (or the first time)

  local window_flags = ImGuiWindowFlags_None

  local is_opened = gh_imgui.window_begin(label, width, height, posx, posy, window_flags, pos_size_flag_first_use_ever, pos_size_flag_first_use_ever)
  return is_opened
end

function imgui_window_begin_close_button(label, width, height, posx, posy, show_window)

  -- Flags for window style, window position and window size.
  --
  local pos_size_flag_always = 1 -- Always set the pos and/or size
  local pos_size_flag_once = 2 -- Set the pos and/or size once per runtime session (only the first call with succeed)
  local pos_size_flag_first_use_ever = 4  -- Set the pos and/or size if the window has no saved data (if doesn't exist in the .ini file)
  local pos_size_flag_appearing = 8  -- Set the pos and/or size if the window is appearing after being hidden/inactive (or the first time)

  local window_flags = ImGuiWindowFlags_None

  local open, show = gh_imgui.window_begin_v2(label, width, height, posx, posy, window_flags, pos_size_flag_once, pos_size_flag_once, show_window)
  return open, show
end

-----------------------------------------------------------------------------
--
function imgui_window_begin_close_button_no_collapse(label, width, height, posx, posy, show_window)

  -- Flags for window style, window position and window size.
  --
  local pos_size_flag_always = 1 -- Always set the pos and/or size
  local pos_size_flag_once = 2 -- Set the pos and/or size once per runtime session (only the first call with succeed)
  local pos_size_flag_first_use_ever = 4  -- Set the pos and/or size if the window has no saved data (if doesn't exist in the .ini file)
  local pos_size_flag_appearing = 8  -- Set the pos and/or size if the window is appearing after being hidden/inactive (or the first time)

  local window_flags = ImGuiWindowFlags_NoCollapse

  local open, show = gh_imgui.window_begin_v2(label, width, height, posx, posy, window_flags, pos_size_flag_once, pos_size_flag_once, show_window)
  return open, show
end



-----------------------------------------------------------------------------
--
function imgui_window_begin_close_button_no_collapse_no_resize(label, width, height, posx, posy, show_window)

  -- Flags for window style, window position and window size.
  --
  local pos_size_flag_always = 1 -- Always set the pos and/or size
  local pos_size_flag_once = 2 -- Set the pos and/or size once per runtime session (only the first call with succeed)
  local pos_size_flag_first_use_ever = 4  -- Set the pos and/or size if the window has no saved data (if doesn't exist in the .ini file)
  local pos_size_flag_appearing = 8  -- Set the pos and/or size if the window is appearing after being hidden/inactive (or the first time)

  local window_flags = ImGuiWindowFlags_NoCollapse + ImGuiWindowFlags_NoResize

  local open, show = gh_imgui.window_begin_v2(label, width, height, posx, posy, window_flags, pos_size_flag_always, pos_size_flag_always, show_window)
  return open, show
end


-----------------------------------------------------------------------------
--
function imgui_window_begin_pos_size_once(label, width, height, posx, posy)

  -- Flags for window style, window position and window size.
  --
  local pos_size_flag_always = 1 -- Always set the pos and/or size
  local pos_size_flag_once = 2 -- Set the pos and/or size once per runtime session (only the first call with succeed)
  local pos_size_flag_first_use_ever = 4  -- Set the pos and/or size if the window has no saved data (if doesn't exist in the .ini file)
  local pos_size_flag_appearing = 8  -- Set the pos and/or size if the window is appearing after being hidden/inactive (or the first time)

  local window_flags = ImGuiWindowFlags_None

  local is_opened = gh_imgui.window_begin(label, width, height, posx, posy, window_flags, pos_size_flag_once, pos_size_flag_once)
  return is_opened
end

-----------------------------------------------------------------------------
--
function imgui_window_begin_pos_size_always(label, width, height, posx, posy)

  -- Flags for window style, window position and window size.
  --
  local pos_size_flag_always = 1 -- Always set the pos and/or size
  local pos_size_flag_once = 2 -- Set the pos and/or size once per runtime session (only the first call with succeed)
  local pos_size_flag_first_use_ever = 4  -- Set the pos and/or size if the window has no saved data (if doesn't exist in the .ini file)
  local pos_size_flag_appearing = 8  -- Set the pos and/or size if the window is appearing after being hidden/inactive (or the first time)

  local window_flags = ImGuiWindowFlags_NoResize + ImGuiWindowFlags_NoMove

  local is_opened = gh_imgui.window_begin(label, width, height, posx, posy, window_flags, pos_size_flag_always, pos_size_flag_always)
  return is_opened
end

function imgui_window_begin_no_collapse(label, width, height, posx, posy)

  -- Flags for window style, window position and window size.
  --
  local pos_size_flag_always = 1 -- Always set the pos and/or size
  local pos_size_flag_once = 2 -- Set the pos and/or size once per runtime session (only the first call with succeed)
  local pos_size_flag_first_use_ever = 4  -- Set the pos and/or size if the window has no saved data (if doesn't exist in the .ini file)
  local pos_size_flag_appearing = 8  -- Set the pos and/or size if the window is appearing after being hidden/inactive (or the first time)

  local window_flags = ImGuiWindowFlags_NoResize + ImGuiWindowFlags_NoMove + ImGuiWindowFlags_NoCollapse

  local is_opened = gh_imgui.window_begin(label, width, height, posx, posy, window_flags, pos_size_flag_always, pos_size_flag_always)
  return is_opened
end



-----------------------------------------------------------------------------
--
function imgui_window_begin_no_titlebar(label, width, height, posx, posy)

  -- Flags for window style, window position and window size.
  --
  local pos_size_flag_always = 1 -- Always set the pos and/or size
  local pos_size_flag_once = 2 -- Set the pos and/or size once per runtime session (only the first call with succeed)
  local pos_size_flag_first_use_ever = 4  -- Set the pos and/or size if the window has no saved data (if doesn't exist in the .ini file)
  local pos_size_flag_appearing = 8  -- Set the pos and/or size if the window is appearing after being hidden/inactive (or the first time)

  local window_flags = ImGuiWindowFlags_NoTitleBar + ImGuiWindowFlags_NoResize + ImGuiWindowFlags_NoMove

  local is_opened = gh_imgui.window_begin(label, width, height, posx, posy, window_flags, pos_size_flag_always, pos_size_flag_always)
  return is_opened
end


-----------------------------------------------------------------------------
--
function imgui_window_begin_no_titlebar_v2(label, width, height, posx, posy, additional_flags)

  -- Flags for window style, window position and window size.
  --
  local pos_size_flag_always = 1 -- Always set the pos and/or size
  local pos_size_flag_once = 2 -- Set the pos and/or size once per runtime session (only the first call with succeed)
  local pos_size_flag_first_use_ever = 4  -- Set the pos and/or size if the window has no saved data (if doesn't exist in the .ini file)
  local pos_size_flag_appearing = 8  -- Set the pos and/or size if the window is appearing after being hidden/inactive (or the first time)

  local window_flags = ImGuiWindowFlags_NoTitleBar + ImGuiWindowFlags_NoResize + ImGuiWindowFlags_NoMove + additional_flags

  local is_opened = gh_imgui.window_begin(label, width, height, posx, posy, window_flags, pos_size_flag_always, pos_size_flag_always)
  return is_opened
end



-----------------------------------------------------------------------------
--
function imgui_is_hovered()
  local hovered = false
  if (gh_imgui.is_any_window_hovered() == 1) then
    hovered = true
  end
  if (gh_imgui.is_any_item_hovered() == 1) then
    hovered = true
  end
  return hovered
end


-----------------------------------------------------------------------------
--
function imgui_window_end()
  gh_imgui.window_end()
end


-----------------------------------------------------------------------------
--
function imgui_vertical_space()
  --gh_imgui.widget(IMGUI_WIDGET_VERTICAL_SPACING)
  gh_imgui.spacing()
end  

function imgui_vertical_space_v2(n)
  for i=1, n do
    --gh_imgui.widget(IMGUI_WIDGET_VERTICAL_SPACING)
    gh_imgui.spacing()
  end
end  

-----------------------------------------------------------------------------
--
function imgui_separator()
  --gh_imgui.widget(IMGUI_WIDGET_SEPARATOR)
  gh_imgui.separator()
end  

-----------------------------------------------------------------------------
--
function imgui_same_line()
  gh_imgui.same_line(0, 10)
  --gh_imgui.widget(IMGUI_WIDGET_SAME_LINE)
end  

-----------------------------------------------------------------------------
--
function imgui_bullet()
  --gh_imgui.widget(IMGUI_WIDGET_BULLET)
  gh_imgui.bullet()
end  




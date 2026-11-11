
--[[
-- Defines for ImPlot.
--]]




-- ImPlot 0.7+
-- ImPlotFlags_None          = 0 --// default
-- ImPlotFlags_NoLegend      = 1 --// the top-left legend will not be displayed
-- ImPlotFlags_NoMenus       = 2 --// the user will not be able to open context menus with double-right click
-- ImPlotFlags_NoBoxSelect   = 4 --// the user will not be able to box-select with right-mouse
-- ImPlotFlags_NoMousePos    = 8 --// the mouse position, in plot coordinates, will not be displayed in the bottom-right
-- ImPlotFlags_NoHighlight   = 16 --// plot items will not be highlighted when their legend entry is hovered
-- ImPlotFlags_NoChild       = 32 --// a child window region will not be used to capture mouse scroll (can boost performance for single ImGui window applications)
-- ImPlotFlags_YAxis2        = 64 --// enable a 2nd y-axis on the right side
-- ImPlotFlags_YAxis3        = 128 --// enable a 3rd y-axis on the right side
-- ImPlotFlags_Query         = 256 --// the user will be able to draw query rects with middle-mouse
-- ImPlotFlags_Crosshairs    = 512 --// the default mouse cursor will be replaced with a crosshair when hovered
-- ImPlotFlags_AntiAliased   = 1024 --// plot lines will be software anti-aliased (not recommended for density plots, prefer MSAA)

-- ImPlot 0.5
-- ImPlotFlags_MousePos = 1
-- ImPlotFlags_Legend = 2
-- ImPlotFlags_Highlight = 4
-- ImPlotFlags_BoxSelect = 8
-- ImPlotFlags_Query = 16
-- ImPlotFlags_ContextMenu = 32
-- ImPlotFlags_Crosshairs  = 64
-- ImPlotFlags_AntiAliased = 128
-- ImPlotFlags_NoChild     = 256
-- ImPlotFlags_YAxis2      = 512
-- ImPlotFlags_YAxis3      = 1024
-- ImPlotFlags_Default     = ImPlotFlags_MousePos + ImPlotFlags_Legend + ImPlotFlags_Highlight + ImPlotFlags_BoxSelect + ImPlotFlags_ContextMenu




-- ImPlot 0.10+
-- ImPlotFlags_None          = 0 --// default
-- ImPlotFlags_NoTitle       = 1 --// the plot title will not be displayed (titles are also hidden if preceeded by double hashes, e.g. "##MyPlot")
-- ImPlotFlags_NoLegend      = 2 --// the top-left legend will not be displayed
-- ImPlotFlags_NoMenus       = 4 --// the user will not be able to open context menus with double-right click
-- ImPlotFlags_NoBoxSelect   = 8 --// the user will not be able to box-select with right-mouse
-- ImPlotFlags_NoMousePos    = 16 --// the mouse position, in plot coordinates, will not be displayed in the bottom-right
-- ImPlotFlags_NoHighlight   = 32 --// plot items will not be highlighted when their legend entry is hovered
-- ImPlotFlags_NoChild       = 64 --// a child window region will not be used to capture mouse scroll (can boost performance for single ImGui window applications)
-- ImPlotFlags_Equal         = 128 --// primary x and y axes will be constrained to have the same units/pixel (does not apply to auxiliary y-axes)
-- ImPlotFlags_YAxis2        = 256 --// enable a 2nd y-axis on the right side
-- ImPlotFlags_YAxis3        = 512 --// enable a 3rd y-axis on the right side
-- ImPlotFlags_Query         = 1024 --// the user will be able to draw query rects with middle-mouse
-- ImPlotFlags_Crosshairs    = 2048 --// the default mouse cursor will be replaced with a crosshair when hovered
-- ImPlotFlags_AntiAliased   = 4096 --// plot lines will be software anti-aliased (not recommended for density plots, prefer MSAA)

-- ImPlot 0.15
-- ImPlotFlags_None          = 0 --// default
-- ImPlotFlags_NoTitle       = 1 --  // the plot title will not be displayed (titles are also hidden if preceeded by double hashes, e.g. "##MyPlot")
-- ImPlotFlags_NoLegend      = 2 --  // the legend will not be displayed
-- ImPlotFlags_NoMouseText   = 4 --  // the mouse position, in plot coordinates, will not be displayed inside of the plot
-- ImPlotFlags_NoInputs      = 8 --  // the user will not be able to interact with the plot
-- ImPlotFlags_NoMenus       = 16 --  // the user will not be able to open context menus
-- ImPlotFlags_NoBoxSelect   = 32 --  // the user will not be able to box-select
-- ImPlotFlags_NoChild       = 64 --  // a child window region will not be used to capture mouse scroll (can boost performance for single ImGui window applications)
-- ImPlotFlags_NoFrame       = 128 --  // the ImGui frame will not be rendered
-- ImPlotFlags_Equal         = 256 --  // x and y axes pairs will be constrained to have the same units/pixel
-- ImPlotFlags_Crosshairs    = 512 --  // the default mouse cursor will be replaced with a crosshair when hovered
-- ImPlotFlags_CanvasOnly    = ImPlotFlags_NoTitle + ImPlotFlags_NoLegend + ImPlotFlags_NoMenus + ImPlotFlags_NoBoxSelect + ImPlotFlags_NoMouseText


-- ImPlot 1.0
ImPlotFlags_None          = 0 --// default
ImPlotFlags_NoTitle       = 1 --  // the plot title will not be displayed (titles are also hidden if preceeded by double hashes, e.g. "##MyPlot")
ImPlotFlags_NoLegend      = 2 --  // the legend will not be displayed
ImPlotFlags_NoMouseText   = 4 --  // the mouse position, in plot coordinates, will not be displayed inside of the plot
ImPlotFlags_NoInputs      = 8 --  // the user will not be able to interact with the plot
ImPlotFlags_NoMenus       = 16 --  // the user will not be able to open context menus
ImPlotFlags_NoBoxSelect   = 32 --  // the user will not be able to box-select
ImPlotFlags_NoFrame       = 64 --  // the ImGui frame will not be rendered
ImPlotFlags_Equal         = 128 --  // x and y axes pairs will be constrained to have the same units/pixel
ImPlotFlags_Crosshairs    = 256 --  // the default mouse cursor will be replaced with a crosshair when hovered
ImPlotFlags_CanvasOnly    = ImPlotFlags_NoTitle + ImPlotFlags_NoLegend + ImPlotFlags_NoMenus + ImPlotFlags_NoBoxSelect + ImPlotFlags_NoMouseText





-- ImPlot 0.10
-- ImPlotAxisFlags_None          = 0 --// default
-- ImPlotAxisFlags_NoLabel       = 1 --// the axis label will not be displayed (axis labels also hidden if the supplied string name is NULL)
-- ImPlotAxisFlags_NoGridLines   = 2 --// no grid lines will be displayed
-- ImPlotAxisFlags_NoTickMarks   = 4 --// no tick marks will be displayed
-- ImPlotAxisFlags_NoTickLabels  = 8 --// no text labels will be displayed
-- ImPlotAxisFlags_LogScale      = 16 --// a logartithmic (base 10) axis scale will be used (mutually exclusive with ImPlotAxisFlags_Time)
-- ImPlotAxisFlags_Time          = 32 --// axis will display date/time formatted labels (mutually exclusive with ImPlotAxisFlags_LogScale)
-- ImPlotAxisFlags_Invert        = 64 --// the axis will be inverted
-- ImPlotAxisFlags_NoInitialFit  = 128 --// axis will not be initially fit to data extents on the first rendered frame (also the case if SetNextPlotLimits explicitly called)
-- ImPlotAxisFlags_AutoFit       = 256 --// axis will be auto-fitting to data extents
-- ImPlotAxisFlags_LockMin       = 512 --// the axis minimum value will be locked when panning/zooming
-- ImPlotAxisFlags_LockMax       = 1024 --// the axis maximum value will be locked when panning/zooming
-- ImPlotAxisFlags_Lock          = ImPlotAxisFlags_LockMin + ImPlotAxisFlags_LockMax
-- ImPlotAxisFlags_NoDecorations = ImPlotAxisFlags_NoGridLines + ImPlotAxisFlags_NoTickMarks + ImPlotAxisFlags_NoTickLabels



-- New in ImPlot 0.15 / 1.0
-- for gh_imgui.implot_setup_axis()
ImPlotAxisFlags_None          = 0 --  // default
ImPlotAxisFlags_NoLabel       = 1 --  // the axis label will not be displayed (axis labels are also hidden if the supplied string name is NULL)
ImPlotAxisFlags_NoGridLines   = 2 --  // no grid lines will be displayed
ImPlotAxisFlags_NoTickMarks   = 4 --  // no tick marks will be displayed
ImPlotAxisFlags_NoTickLabels  = 8 --  // no text labels will be displayed
ImPlotAxisFlags_NoInitialFit  = 16 -- // axis will not be initially fit to data extents on the first rendered frame
ImPlotAxisFlags_NoMenus       = 32 -- // the user will not be able to open context menus with right-click
ImPlotAxisFlags_NoSideSwitch  = 64 -- // the user will not be able to switch the axis side by dragging it
ImPlotAxisFlags_NoHighlight   = 128 --  // the axis will not have its background highlighted when hovered or held
ImPlotAxisFlags_Opposite      = 256 --  // axis ticks and labels will be rendered on the conventionally opposite side (i.e, right or top)
ImPlotAxisFlags_Foreground    = 512 --  // grid lines will be displayed in the foreground (i.e. on top of data) instead of the background
ImPlotAxisFlags_Invert        = 1024 -- // the axis will be inverted
ImPlotAxisFlags_AutoFit       = 2048 -- // axis will be auto-fitting to data extents
ImPlotAxisFlags_RangeFit      = 4096 -- // axis will only fit points if the point is in the visible range of the **orthogonal** axis
ImPlotAxisFlags_PanStretch    = 8192 -- // panning in a locked or constrained state will cause the axis to stretch if possible
ImPlotAxisFlags_LockMin       = 16384 -- // the axis minimum value will be locked when panning/zooming
ImPlotAxisFlags_LockMax       = 32768 -- // the axis maximum value will be locked when panning/zooming
ImPlotAxisFlags_TickLabelsInside = 65536 -- // axis tick labels will be drawn inside the plot area (applies only to the innermost axes)
ImPlotAxisFlags_Lock          = ImPlotAxisFlags_LockMin + ImPlotAxisFlags_LockMax
ImPlotAxisFlags_NoDecorations = ImPlotAxisFlags_NoLabel + ImPlotAxisFlags_NoGridLines + ImPlotAxisFlags_NoTickMarks + ImPlotAxisFlags_NoTickLabels
ImPlotAxisFlags_AuxDefault    = ImPlotAxisFlags_NoGridLines + ImPlotAxisFlags_Opposite



-- ImPlot 0.5
-- ImPlotAxisFlags_GridLines = 1
-- ImPlotAxisFlags_TickMarks = 2
-- ImPlotAxisFlags_TickLabels = 4
-- ImPlotAxisFlags_Invert = 8
-- ImPlotAxisFlags_LockMin = 16
-- ImPlotAxisFlags_LockMax = 32
-- ImPlotAxisFlags_LogScale = 64
-- ImPlotAxisFlags_Scientific = 128
-- ImPlotAxisFlags_Default = 7
-- ImPlotAxisFlags_Auxiliary = 6










-- ImPlot 0.10
-- ImPlotCol_Line = 0 --// plot line/outline color (defaults to next unused color in current colormap)
-- ImPlotCol_Fill = 1 -- plot fill color for bars (defaults to the current line color)
-- ImPlotCol_MarkerOutline = 2 -- marker outline color (defaults to the current line color)
-- ImPlotCol_MarkerFill = 3 -- marker fill color (defaults to the current line color)
-- ImPlotCol_ErrorBar = 4 -- error bar color (defaults to ImGuiCol_Text)
-- ImPlotCol_FrameBg = 5 -- plot frame background color (defaults to ImGuiCol_FrameBg)
-- ImPlotCol_PlotBg = 6 -- plot area background color (defaults to ImGuiCol_WindowBg)
-- ImPlotCol_PlotBorder = 7 -- plot area border color (defaults to ImGuiCol_Text)
-- ImPlotCol_LegendBg = 8 -- // legend background color (defaults to ImGuiCol_PopupBg)
-- ImPlotCol_LegendBorder = 9 -- // legend border color (defaults to ImPlotCol_PlotBorder)
-- ImPlotCol_LegendText = 10 -- // legend text color (defaults to ImPlotCol_InlayText)
-- ImPlotCol_TitleText = 11 -- // plot title text color (defaults to ImGuiCol_Text)
-- ImPlotCol_InlayText = 12 -- // color of text appearing inside of plots (defaults to ImGuiCol_Text)
-- ImPlotCol_XAxis = 13 -- // x-axis label and tick lables color (defaults to ImGuiCol_Text)
-- ImPlotCol_XAxisGrid = 14 -- // x-axis grid color (defaults to 25% ImPlotCol_XAxis)
-- ImPlotCol_YAxis = 15 -- // y-axis label and tick labels color (defaults to ImGuiCol_Text)
-- ImPlotCol_YAxisGrid = 16 -- // y-axis grid color (defaults to 25% ImPlotCol_YAxis)
-- ImPlotCol_YAxis2 = 17 -- // 2nd y-axis label and tick labels color (defaults to ImGuiCol_Text)
-- ImPlotCol_YAxisGrid2 = 18 -- // 2nd y-axis grid/label color (defaults to 25% ImPlotCol_YAxis2)
-- ImPlotCol_YAxis3 = 19 -- // 3rd y-axis label and tick labels color (defaults to ImGuiCol_Text)
-- ImPlotCol_YAxisGrid3 = 20 -- // 3rd y-axis grid/label color (defaults to 25% ImPlotCol_YAxis3)
-- ImPlotCol_Selection = 21 -- // box-selection color (defaults to yellow)
-- ImPlotCol_Query = 22 -- // box-query color (defaults to green)
-- ImPlotCol_Crosshairs = 23 -- // crosshairs color (defaults to ImPlotCol_PlotBorder)

-- ImPlot 0.15
-- ImPlotCol_Line = 0 --// plot line/outline color (defaults to next unused color in current colormap)
-- ImPlotCol_Fill = 1 -- plot fill color for bars (defaults to the current line color)
-- ImPlotCol_MarkerOutline = 2 -- marker outline color (defaults to the current line color)
-- ImPlotCol_MarkerFill = 3 -- marker fill color (defaults to the current line color)
-- ImPlotCol_ErrorBar = 4 -- error bar color (defaults to ImGuiCol_Text)
-- ImPlotCol_FrameBg = 5 -- plot frame background color (defaults to ImGuiCol_FrameBg)
-- ImPlotCol_PlotBg = 6 -- plot area background color (defaults to ImGuiCol_WindowBg)
-- ImPlotCol_PlotBorder = 7 -- plot area border color (defaults to ImGuiCol_Text)
-- ImPlotCol_LegendBg = 8 -- // legend background color (defaults to ImGuiCol_PopupBg)
-- ImPlotCol_LegendBorder = 9 -- // legend border color (defaults to ImPlotCol_PlotBorder)
-- ImPlotCol_LegendText = 10 -- // legend text color (defaults to ImPlotCol_InlayText)
-- ImPlotCol_TitleText = 11 -- // plot title text color (defaults to ImGuiCol_Text)
-- ImPlotCol_InlayText = 12 -- // color of text appearing inside of plots (defaults to ImGuiCol_Text)
-- ImPlotCol_AxisText = 13 --   // axis label and tick lables color (defaults to ImGuiCol_Text)
-- ImPlotCol_AxisGrid = 14 --   // axis grid color (defaults to 25% ImPlotCol_AxisText)
-- ImPlotCol_AxisTick = 15 --  // axis tick color (defaults to AxisGrid)
-- ImPlotCol_AxisBg = 16 --   // background color of axis hover region (defaults to transparent)
-- ImPlotCol_AxisBgHovered = 17 -- // axis hover color (defaults to ImGuiCol_ButtonHovered)
-- ImPlotCol_AxisBgActive = 18 -- // axis active color (defaults to ImGuiCol_ButtonActive)
-- ImPlotCol_Selection = 19 --   // box-selection color (defaults to yellow)
-- ImPlotCol_Crosshairs = 20 -- // crosshairs color (defaults to ImPlotCol_PlotBorder)


-- ImPlot 1.0
ImPlotCol_FrameBg = 0 --// plot frame background color (defaults to ImGuiCol_FrameBg)
ImPlotCol_PlotBg = 1  --// plot area background color (defaults to ImGuiCol_WindowBg)
ImPlotCol_PlotBorder = 2 --// plot area border color (defaults to ImGuiCol_Border)
ImPlotCol_LegendBg = 3 --// legend background color (defaults to ImGuiCol_PopupBg)
ImPlotCol_LegendBorder = 4 --// legend border color (defaults to ImPlotCol_PlotBorder)
ImPlotCol_LegendText = 5 --// legend text color (defaults to ImPlotCol_InlayText)
ImPlotCol_TitleText = 6 -- // plot title text color (defaults to ImGuiCol_Text)
ImPlotCol_InlayText = 7 --// color of text appearing inside of plots (defaults to ImGuiCol_Text)
ImPlotCol_AxisText = 8 --// axis label and tick labels color (defaults to ImGuiCol_Text)
ImPlotCol_AxisGrid = 9 --// axis grid color (defaults to 25% ImPlotCol_AxisText)
ImPlotCol_AxisTick = 10 --// axis tick color (defaults to AxisGrid)
ImPlotCol_AxisBg = 11 --// background color of axis hover region (defaults to transparent)
ImPlotCol_AxisBgHovered = 12 --// axis hover color (defaults to ImGuiCol_ButtonHovered)
ImPlotCol_AxisBgActive = 13 --// axis active color (defaults to ImGuiCol_ButtonActive)
ImPlotCol_Selection = 14 --// box-selection color (defaults to yellow)
ImPlotCol_Crosshairs = 15 --// crosshairs color (defaults to ImPlotCol_PlotBorder)






-- ImPlot 0.15/0.17
-- ImPlotMarker_None        = -1
-- ImPlotMarker_Circle      = 0
-- ImPlotMarker_Square      = 1
-- ImPlotMarker_Diamond     = 2
-- ImPlotMarker_Up          = 3
-- ImPlotMarker_Down        = 4
-- ImPlotMarker_Left        = 5
-- ImPlotMarker_Right       = 6
-- ImPlotMarker_Cross       = 7
-- ImPlotMarker_Plus        = 8
-- ImPlotMarker_Asterisk    = 9




-- ImPlot 1.0
ImPlotMarker_None = -2 --// no marker
ImPlotMarker_Auto = -1 --// automatic marker selection
ImPlotMarker_Circle = 0 --// a circle marker (default)
ImPlotMarker_Square = 1 --// a square maker
ImPlotMarker_Diamond = 2 --// a diamond marker
ImPlotMarker_Up = 3 --// an upward-pointing triangle marker
ImPlotMarker_Down = 4 --// an downward-pointing triangle marker
ImPlotMarker_Left = 5 --// an leftward-pointing triangle marker
ImPlotMarker_Right = 6 --// an rightward-pointing triangle marker
ImPlotMarker_Cross = 7 --// a cross marker (not fill-able)
ImPlotMarker_Plus = 8 --// a plus marker (not fill-able)
ImPlotMarker_Asterisk = 9 --// a asterisk marker (not fill-able)
ImPlotMarker_Vertical = 10 -- // a vertical line marker (not fill-able)
ImPlotMarker_Horizontal = 11 -- // a horizontal line marker (not fill-able)





-- ImPlot 0.15/0.17
-- ImPlotStyleVar_LineWeight = 0 -- // float,  plot item line weight in pixels
-- ImPlotStyleVar_Marker = 1 -- // int,    marker specification
-- ImPlotStyleVar_MarkerSize = 2 -- // float,  marker size in pixels (roughly the marker's "radius")
-- ImPlotStyleVar_MarkerWeight = 3 -- // float,  plot outline weight of markers in pixels
-- ImPlotStyleVar_FillAlpha = 4 -- // float,  alpha modifier applied to all plot item fills
-- ImPlotStyleVar_ErrorBarSize = 5 -- // float,  error bar whisker width in pixels
-- ImPlotStyleVar_ErrorBarWeight = 6 -- // float,  error bar whisker weight in pixels
-- ImPlotStyleVar_DigitalBitHeight = 7 -- // float,  digital channels bit height (at 1) in pixels
-- ImPlotStyleVar_DigitalBitGap = 8 -- // float,  digital channels bit padding gap in pixels
-- ImPlotStyleVar_PlotBorderSize = 9 -- // float,  thickness of border around plot area
-- ImPlotStyleVar_MinorAlpha = 10 -- // float,  alpha multiplier applied to minor axis grid lines
-- ImPlotStyleVar_MajorTickLen = 11 -- // ImVec2, major tick lengths for X and Y axes
-- ImPlotStyleVar_MinorTickLen = 12 -- // ImVec2, minor tick lengths for X and Y axes
-- ImPlotStyleVar_MajorTickSize = 13 -- // ImVec2, line thickness of major ticks
-- ImPlotStyleVar_MinorTickSize = 14 -- // ImVec2, line thickness of minor ticks
-- ImPlotStyleVar_MajorGridSize = 15 -- // ImVec2, line thickness of major grid lines
-- ImPlotStyleVar_MinorGridSize = 16 -- // ImVec2, line thickness of minor grid lines
-- ImPlotStyleVar_PlotPadding = 17 -- // ImVec2, padding between widget frame and plot area and/or labels
-- ImPlotStyleVar_LabelPadding = 18 -- // ImVec2, padding between axes labels, tick labels, and plot edge
-- ImPlotStyleVar_LegendPadding = 19 -- // ImVec2, legend padding from top-left of plot
-- ImPlotStyleVar_LegendInnerPadding = 20 --// ImVec2, legend inner padding from legend edges
-- ImPlotStyleVar_LegendSpacing = 21 --// ImVec2, spacing between legend entries
-- ImPlotStyleVar_MousePosPadding = 22 --// ImVec2, padding between plot edge and interior info text
-- ImPlotStyleVar_AnnotationPadding = 23 --// ImVec2, text padding around annotation labels
-- ImPlotStyleVar_FitPadding = 24 --// ImVec2, additional fit padding as a percentage of the fit extents (e.g. ImVec2(0.1f,0.1f) adds 10% to the fit extents of X and Y)
-- ImPlotStyleVar_PlotDefaultSize = 25 --// ImVec2, default size used when ImVec2(0,0) is passed to BeginPlot
-- ImPlotStyleVar_PlotMinSize = 26 -- // ImVec2, minimum size plot frame can be when shrunk



-- ImPlot 1.0
ImPlotStyleVar_PlotDefaultSize = 0 --// ImVec2, default size used when ImVec2(0,0) is passed to BeginPlot
ImPlotStyleVar_PlotMinSize = 1 --// ImVec2, minimum size plot frame can be when shrunk
ImPlotStyleVar_PlotBorderSize = 2 --// float,  thickness of border around plot area
ImPlotStyleVar_MinorAlpha = 3 --// float,  alpha multiplier applied to minor axis grid lines
ImPlotStyleVar_MajorTickLen = 4 --// ImVec2, major tick lengths for X and Y axes
ImPlotStyleVar_MinorTickLen = 5 --// ImVec2, minor tick lengths for X and Y axes
ImPlotStyleVar_MajorTickSize = 6 --// ImVec2, line thickness of major ticks
ImPlotStyleVar_MinorTickSize = 7 --// ImVec2, line thickness of minor ticks
ImPlotStyleVar_MajorGridSize = 8 --// ImVec2, line thickness of major grid lines
ImPlotStyleVar_MinorGridSize = 9 --// ImVec2, line thickness of minor grid lines
ImPlotStyleVar_PlotPadding = 10 --// ImVec2, padding between widget frame and plot area, labels, or outside legends (i.e. main padding)
ImPlotStyleVar_LabelPadding = 11 --// ImVec2, padding between axes labels, tick labels, and plot edge
ImPlotStyleVar_LegendPadding = 12 --// ImVec2, legend padding from plot edges
ImPlotStyleVar_LegendInnerPadding = 13 --// ImVec2, legend inner padding from legend edges
ImPlotStyleVar_LegendSpacing = 14 --// ImVec2, spacing between legend entries
ImPlotStyleVar_MousePosPadding = 15 --// ImVec2, padding between plot edge and interior info text
ImPlotStyleVar_AnnotationPadding = 16 --// ImVec2, text padding around annotation labels
ImPlotStyleVar_FitPadding = 17 --// ImVec2, additional fit padding as a percentage of the fit extents (e.g. ImVec2(0.1f,0.1f) adds 10% to the fit extents of X and Y)
ImPlotStyleVar_DigitalPadding = 18 --// float,  digital plot padding from bottom in pixels
ImPlotStyleVar_DigitalSpacing = 19 --// float,  digital plot spacing gap in pixels




--[[
ImPlotColormap_xxxxx in ImPlot 0.8 and 0.9

ImPlotColormap_Default  = 0
ImPlotColormap_Deep     = 1
ImPlotColormap_Dark     = 2
ImPlotColormap_Pastel   = 3
ImPlotColormap_Paired   = 4
ImPlotColormap_Viridis  = 5
ImPlotColormap_Plasma   = 6
ImPlotColormap_Hot      = 7
ImPlotColormap_Cool     = 8
ImPlotColormap_Pink     = 9
ImPlotColormap_Jet      = 10
--]]


-- ImPlotColormap_xxxxx in ImPlot 0.10+
-- ImPlot 0.15 / 0.17 / 1.0
ImPlotColormap_Deep     = 0 --// a.k.a. seaborn deep             (qual=true,  n=10) (default)
ImPlotColormap_Dark     = 1 --// a.k.a. matplotlib "Set1"        (qual=true,  n=9 )
ImPlotColormap_Pastel   = 2 --// a.k.a. matplotlib "Pastel1"     (qual=true,  n=9 )
ImPlotColormap_Paired   = 3 --// a.k.a. matplotlib "Paired"      (qual=true,  n=12)
ImPlotColormap_Viridis  = 4 --// a.k.a. matplotlib "viridis"     (qual=false, n=11)
ImPlotColormap_Plasma   = 5 --// a.k.a. matplotlib "plasma"      (qual=false, n=11)
ImPlotColormap_Hot      = 6 --// a.k.a. matplotlib/MATLAB "hot"  (qual=false, n=11)
ImPlotColormap_Cool     = 7 --// a.k.a. matplotlib/MATLAB "cool" (qual=false, n=11)
ImPlotColormap_Pink     = 8 --// a.k.a. matplotlib/MATLAB "pink" (qual=false, n=11)
ImPlotColormap_Jet      = 9 --// a.k.a. MATLAB "jet"             (qual=false, n=11)
ImPlotColormap_Twilight = 10 --// a.k.a. matplotlib "twilight"    (qual=false, n=11)
ImPlotColormap_RdBu     = 11 --// red/blue, Color Brewer          (qual=false, n=11)
ImPlotColormap_BrBG     = 12 --// brown/blue-green, Color Brewer  (qual=false, n=11)
ImPlotColormap_PiYG     = 13 --// pink/yellow-green, Color Brewer (qual=false, n=11)
ImPlotColormap_Spectral = 14 --// color spectrum, Color Brewer    (qual=false, n=11)
ImPlotColormap_Greys    = 15 --// white/black                     (qual=false, n=2 )







ImGuiCond_None          = 0
ImGuiCond_Always        = 1
ImGuiCond_Once          = 2
ImGuiCond_FirstUseEver  = 4
ImGuiCond_Appearing     = 8



-- ImPlot 0.15 / 1.0
-- horizontal axes
ImAxis_X1 = 0 -- // enabled by default
ImAxis_X2 = 1 -- // disabled by default
ImAxis_X3 = 2 -- // disabled by default
-- vertical axes
ImAxis_Y1 = 3 -- // enabled by default
ImAxis_Y2 = 4 -- // disabled by default
ImAxis_Y3 = 5 -- // disabled by default


-- ImPlot 0.15 / 0.17
-- for gh_imgui.implot_setup_legend()
ImPlotLegendFlags_None            = 0 --   // default
ImPlotLegendFlags_NoButtons       = 1 -- // legend icons will not function as hide/show buttons
ImPlotLegendFlags_NoHighlightItem = 2 -- // plot items will not be highlighted when their legend entry is hovered
ImPlotLegendFlags_NoHighlightAxis = 4 -- // axes will not be highlighted when legend entries are hovered (only relevant if x/y-axis count > 1)
ImPlotLegendFlags_NoMenus         = 8 -- // the user will not be able to open context menus with right-click
ImPlotLegendFlags_Outside         = 16 -- // legend will be rendered outside of the plot area
ImPlotLegendFlags_Horizontal      = 32 -- // legend entries will be displayed horizontally
ImPlotLegendFlags_Sort            = 64 -- // legend entries will be displayed in alphabetical order
-- ImPlot 1.0
ImPlotLegendFlags_Reverse         = 128 --// legend entries will be displayed in reverse order


-- ImPlot 0.15 / 1.0
-- for gh_imgui.implot_setup_mouse_text()
ImPlotMouseTextFlags_None        = 0 -- // default
ImPlotMouseTextFlags_NoAuxAxes   = 1 -- // only show the mouse position for primary axes
ImPlotMouseTextFlags_NoFormat    = 2 -- // axes label formatters won't be used to render text
ImPlotMouseTextFlags_ShowAlways  = 4 -- // always display mouse position even if plot not hovered

-- ImPlot 0.15 / 1.0
-- for gh_imgui.implot_setup_axis_scale()
ImPlotScale_Linear = 0 -- // default linear scale
ImPlotScale_Time = 1 -- // date/time scale
ImPlotScale_Log10 = 2 -- // base 10 logartithmic scale
ImPlotScale_SymLog = 3 -- // symmetric log scale


-- ImPlot 0.15 / 1.0
-- for gh_imgui.implot_setup_legend()
-- for gh_imgui.implot_setup_mouse_text()
ImPlotLocation_Center    = 0 --                                      // center-center
ImPlotLocation_North     = 1 --                                      // top-center
ImPlotLocation_South     = 2 --                                      // bottom-center
ImPlotLocation_West      = 4 --                                      // center-left
ImPlotLocation_East      = 8 --                                      // center-right
ImPlotLocation_NorthWest = ImPlotLocation_North + ImPlotLocation_West -- // top-left
ImPlotLocation_NorthEast = ImPlotLocation_North + ImPlotLocation_East -- // top-right
ImPlotLocation_SouthWest = ImPlotLocation_South + ImPlotLocation_West -- // bottom-left
ImPlotLocation_SouthEast = ImPlotLocation_South + ImPlotLocation_East --  // bottom-right



-- ImPlot 0.15 / 1.0
-- for gh_imgui.implot_draw_plotline()
ImPlotLineFlags_None        = 0 --   // default
ImPlotLineFlags_Segments    = 1024 -- // a line segment will be rendered from every two consecutive points
ImPlotLineFlags_Loop        = 2048 -- // the last and first point will be connected to form a closed loop
ImPlotLineFlags_SkipNaN     = 4096 -- // NaNs values will be skipped instead of rendered as missing data
ImPlotLineFlags_NoClip      = 8192 --  // markers (if displayed) on the edge of a plot will not be clipped
ImPlotLineFlags_Shaded      = 16384 -- // a filled region between the line and horizontal origin will be rendered; use PlotShaded for more advanced cases


-- ImPlot 0.15 / 1.0
-- for gh_imgui.implot_draw_plotscatter()
ImPlotScatterFlags_None   = 0 -- // default
ImPlotScatterFlags_NoClip = 1024 -- // markers on the edge of a plot will not be clipped

-- ImPlot 0.15 / 1.0
-- for gh_imgui.implot_draw_plotbars()
ImPlotBarsFlags_None         = 0 --   // default
ImPlotBarsFlags_Horizontal   = 1024 -- // bars will be rendered horizontally on the current y-axis


ImPlotItemFlags_None     = 0 --// default
ImPlotItemFlags_NoLegend = 1 --// the item won't have a legend entry displayed
ImPlotItemFlags_NoFit    = 2 --// the item won't be considered for plot fits


ImPlotScatterFlags_None   = 0 --// default
ImPlotScatterFlags_NoClip = 1024 --// markers on the edge of a plot will not be clipped

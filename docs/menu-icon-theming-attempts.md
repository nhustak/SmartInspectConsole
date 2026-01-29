# Menu Icon Dark Theme - Attempts Log

## Problem
Menu icons (Segoe Fluent Icons via TextBlock in MenuItem.Icon) appear faint/gray in dark theme.
The menu item TEXT is correctly white, but the ICONS remain faint.

## Root Cause (researched)
The MenuItem's default ControlTemplate has a separate ContentPresenter for the Icon area
that does NOT bind TextElement.Foreground to the MenuItem's Foreground property.
So MenuItem.Foreground only affects the Header text, not the Icon content.

## Attempts (all failed)

### 1. Explicit Foreground on each TextBlock
```xml
<MenuItem.Icon>
  <TextBlock Text="&#xE78C;" FontFamily="Segoe Fluent Icons" FontSize="14"
             Foreground="{DynamicResource ForegroundBrush}"/>
</MenuItem.Icon>
```
**Result:** Icons still faint. DynamicResource likely not resolving in ContextMenu's
disconnected visual tree.

### 2. Style.Resources on MenuItem style
```xml
<Style TargetType="MenuItem">
    <Style.Resources>
        <Style TargetType="TextBlock">
            <Setter Property="Foreground" Value="{DynamicResource ForegroundBrush}"/>
        </Style>
    </Style.Resources>
</Style>
```
**Result:** Icons still faint. The implicit TextBlock style inside Style.Resources
doesn't reach the Icon ContentPresenter.

### 3. TextElement.Foreground on ContextMenu style
```xml
<Style TargetType="ContextMenu">
    <Setter Property="TextElement.Foreground" Value="{DynamicResource ForegroundBrush}"/>
</Style>
```
**Result:** Icons still faint. The attached property inheritance may be broken
by the MenuItem's internal template structure.

### 4. Removed all explicit Foreground, relying on inheritance
Removed Foreground from every icon TextBlock. Combined with attempt #3's
TextElement.Foreground on ContextMenu, hoping WPF property inheritance would reach the icons.
```xml
<MenuItem.Icon>
  <TextBlock Text="&#xE78C;" FontFamily="Segoe Fluent Icons" FontSize="14"/>
</MenuItem.Icon>
```
**Result:** Icons still faint. The MenuItem template's Icon ContentPresenter blocks
property inheritance regardless of whether Foreground is set explicitly or inherited.

### 5. Hardcoded color (#D4D4D4) on each TextBlock
```xml
<MenuItem.Icon>
  <TextBlock Text="&#xE78C;" FontFamily="Segoe Fluent Icons" FontSize="14"
             Foreground="#D4D4D4"/>
</MenuItem.Icon>
```
**Result:** Icons STILL faint. This proves the problem is NOT the color value or
DynamicResource resolution. The MenuItem template is likely applying reduced Opacity
to the icon container, making any Foreground color appear muted.

## What HAS NOT been tried
- Custom MenuItem ControlTemplate that binds Icon ContentPresenter's TextElement.Foreground
- Using Image/Path elements instead of TextBlock for icons
- Setting Foreground in code-behind when theme changes
- Using {x:Static} brushes instead of DynamicResource
- Hardcoding a light color (e.g., Foreground="#E0E0E0") instead of DynamicResource

using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace MyTaskManager
{
    public static class Theme
    {
        public static Brush B(string key) { return (Brush)Application.Current.FindResource(key); }
        public static Style S(string key) { return (Style)Application.Current.FindResource(key); }
        public static FontFamily IconFont { get { return (FontFamily)Application.Current.FindResource("IconFont"); } }

        /* Follow a colour token live (for elements built in code that stay on screen) */
        public static T Ref<T>(T e, DependencyProperty p, string key) where T : FrameworkElement { e.SetResourceReference(p, key); return e; }

        public static ResourceDictionary Load() { ResourceDictionary rd = (ResourceDictionary)XamlReader.Parse(Xaml); Fill(rd, false); IsDark = false; return rd; }

        /* Colour tokens. Dark keeps the app's cool slate + indigo identity instead of going flat black:
           surfaces step up in lightness (Bg < Surface < Surface2) the way light mode steps down. */
        static readonly string[] Keys =  { "Bg",      "Surface", "Surface2", "Line",    "LineStrong", "Text",    "Text2",   "Text3",   "Accent",  "AccentSoft", "Ok",      "OkSoft",  "Warn",    "WarnSoft", "Danger",  "DangerSoft", "Hover",     "Press",     "Thumb",   "Tip",     "OnAccent" };
        static readonly string[] Light = { "#F4F5F8", "#FFFFFF", "#F7F8FA",  "#E3E6EC", "#CDD2DB",    "#161A23", "#5A6272", "#666D7B", "#3B5BDB", "#E8EDFF",    "#157A4A", "#E2F5EC", "#8F5300", "#FFF3DC",  "#C0313A", "#FDE8E9",    "#0C000000", "#1A000000", "#C3C8D1", "#1F2430", "#FFFFFF" };
        static readonly string[] Dark =  { "#12151C", "#1A1E27", "#222733",  "#2C3240", "#3B4252",    "#E7EAF1", "#A2AABA", "#7A8295", "#7B93F5", "#222A45",    "#3DBE83", "#16332A", "#E3A445", "#382D18",  "#F07178", "#3A1C21",    "#12FFFFFF", "#22FFFFFF", "#3E4556", "#2C3240", "#10142A" };

        public static bool IsDark { get; private set; }

        /* Also used by the sticky note to give its own controls a palette that suits the note colour */
        public static void Fill(ResourceDictionary rd, bool dark)
        {
            string[] p = dark ? Dark : Light;
            for (int i = 0; i < Keys.Length; i++)
            {
                SolidColorBrush b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(p[i]));
                b.Freeze();
                rd[Keys[i]] = b;
            }
            // context menus and their submenus use these system keys
            rd[SystemColors.MenuBrushKey] = rd["Surface"];
            rd[SystemColors.MenuTextBrushKey] = rd["Text"];
            rd[SystemColors.ControlTextBrushKey] = rd["Text"];
        }

        /* Switch every open window live (all colours are DynamicResource) */
        public static void Apply(bool dark)
        {
            Fill(Application.Current.Resources, dark);
            IsDark = dark;
            foreach (Window w in Application.Current.Windows) TitleBar(w);
        }

        /* "light" | "dark" | "system" (follows the Windows app setting) */
        public static bool WantsDark(string mode)
        {
            if (mode == "dark") return true;
            if (mode != "system") return false;
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(System.IntPtr hwnd, int attr, ref int value, int size);

        /* Dark or light Windows title bar to match (Windows 10 20H1+ / 11; ignored elsewhere) */
        public static void TitleBar(Window w)
        {
            if (w == null || w.WindowStyle == WindowStyle.None) return;
            System.IntPtr h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            if (h == System.IntPtr.Zero) { w.SourceInitialized += delegate { TitleBar(w); }; return; }
            int on = IsDark ? 1 : 0;
            try { if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0) DwmSetWindowAttribute(h, 19, ref on, 4); } catch { }
            // nudge Windows to repaint the caption now
            if (w.IsVisible && w.SizeToContent == SizeToContent.Manual && !double.IsNaN(w.Width)) { double width = w.Width; w.Width = width + 1; w.Width = width; }
        }

        const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <SolidColorBrush x:Key='Bg' Color='#F4F5F8'/>
  <SolidColorBrush x:Key='Surface' Color='#FFFFFF'/>
  <SolidColorBrush x:Key='Surface2' Color='#F7F8FA'/>
  <SolidColorBrush x:Key='Line' Color='#E3E6EC'/>
  <SolidColorBrush x:Key='LineStrong' Color='#CDD2DB'/>
  <SolidColorBrush x:Key='Text' Color='#161A23'/>
  <SolidColorBrush x:Key='Text2' Color='#5A6272'/>
  <SolidColorBrush x:Key='Text3' Color='#666D7B'/>
  <SolidColorBrush x:Key='Accent' Color='#3B5BDB'/>
  <SolidColorBrush x:Key='AccentSoft' Color='#E8EDFF'/>
  <SolidColorBrush x:Key='Ok' Color='#157A4A'/>
  <SolidColorBrush x:Key='OkSoft' Color='#E2F5EC'/>
  <SolidColorBrush x:Key='Warn' Color='#8F5300'/>
  <SolidColorBrush x:Key='WarnSoft' Color='#FFF3DC'/>
  <SolidColorBrush x:Key='Danger' Color='#C0313A'/>
  <SolidColorBrush x:Key='DangerSoft' Color='#FDE8E9'/>
  <FontFamily x:Key='IconFont'>Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>

  <Style x:Key='Card' TargetType='Border'>
    <Setter Property='Background' Value='{DynamicResource Surface}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Line}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='CornerRadius' Value='12'/>
  </Style>

  <Style x:Key='Label' TargetType='TextBlock'>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Foreground' Value='{DynamicResource Text2}'/>
    <Setter Property='Margin' Value='0,0,0,5'/>
  </Style>

  <!-- Buttons: one template, an overlay darkens on hover/press so it works for every colour -->
  <Style x:Key='Btn' TargetType='Button'>
    <Setter Property='Background' Value='{DynamicResource Surface}'/>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource LineStrong}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='14,7'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='HorizontalContentAlignment' Value='Center'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8' SnapsToDevicePixels='True'>
            <Border x:Name='Ov' CornerRadius='7' Background='Transparent' Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' RecognizesAccessKey='False'/>
            </Border>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Ov' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='Ov' Property='Background' Value='{DynamicResource Press}'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='Primary' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Background' Value='{DynamicResource Accent}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Accent}'/>
    <Setter Property='Foreground' Value='{DynamicResource OnAccent}'/>
  </Style>
  <Style x:Key='Ghost' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderBrush' Value='Transparent'/>
    <Setter Property='Foreground' Value='{DynamicResource Text2}'/>
  </Style>
  <Style x:Key='Nav' TargetType='Button' BasedOn='{StaticResource Ghost}'>
    <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
    <Setter Property='Padding' Value='10,9'/>
    <Setter Property='Margin' Value='0,0,0,2'/>
    <Setter Property='FontWeight' Value='Medium'/>
    <Style.Triggers>
      <Trigger Property='Tag' Value='on'>
        <Setter Property='Background' Value='{DynamicResource AccentSoft}'/>
        <Setter Property='Foreground' Value='{DynamicResource Accent}'/>
        <Setter Property='FontWeight' Value='SemiBold'/>
      </Trigger>
    </Style.Triggers>
  </Style>
  <Style x:Key='Chip' TargetType='Button' BasedOn='{StaticResource Ghost}'>
    <Setter Property='Padding' Value='12,0'/>
    <Setter Property='FontWeight' Value='Medium'/>
    <Style.Triggers>
      <Trigger Property='Tag' Value='on'>
        <Setter Property='Background' Value='{DynamicResource Surface}'/>
        <Setter Property='BorderBrush' Value='{DynamicResource Line}'/>
        <Setter Property='Foreground' Value='{DynamicResource Text}'/>
        <Setter Property='FontWeight' Value='SemiBold'/>
      </Trigger>
    </Style.Triggers>
  </Style>
  <Style x:Key='Icon' TargetType='Button' BasedOn='{StaticResource Ghost}'>
    <Setter Property='Width' Value='32'/>
    <Setter Property='Height' Value='32'/>
    <Setter Property='Padding' Value='0'/>
    <Setter Property='FontFamily' Value='{StaticResource IconFont}'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='FontWeight' Value='Normal'/>
  </Style>
  <Style x:Key='NoteBtn' TargetType='Button' BasedOn='{StaticResource Ghost}'>
    <Setter Property='Width' Value='28'/>
    <Setter Property='Height' Value='28'/>
    <Setter Property='Padding' Value='0'/>
    <Setter Property='FontFamily' Value='{StaticResource IconFont}'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='FontWeight' Value='Normal'/>
    <Setter Property='Foreground' Value='#4A4436'/>
    <Style.Triggers>
      <Trigger Property='Tag' Value='on'><Setter Property='Background' Value='#22000000'/></Trigger>
    </Style.Triggers>
  </Style>
  <Style x:Key='Link' TargetType='Button' BasedOn='{StaticResource Ghost}'>
    <Setter Property='Foreground' Value='{DynamicResource Accent}'/>
    <Setter Property='Padding' Value='6,4'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
  </Style>
  <Style x:Key='Field' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Background' Value='{DynamicResource Surface2}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Line}'/>
    <Setter Property='FontWeight' Value='Normal'/>
    <Setter Property='Padding' Value='11,0'/>
    <Setter Property='Height' Value='38'/>
    <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
  </Style>

  <!-- Text input with placeholder (placeholder text goes in Tag) -->
  <Style x:Key='Input' TargetType='TextBox'>
    <Setter Property='Background' Value='{DynamicResource Surface2}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Line}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='Padding' Value='9,0'/>
    <Setter Property='MinHeight' Value='38'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Border x:Name='Bd' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>
            <Grid>
              <TextBlock x:Name='Ph' Text='{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}' Foreground='{DynamicResource Text3}'
                         Margin='{TemplateBinding Padding}' Padding='2,0,2,0' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'
                         Visibility='Collapsed' IsHitTestVisible='False' TextTrimming='CharacterEllipsis'/>
              <ScrollViewer x:Name='PART_ContentHost' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' Focusable='False'/>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='Text' Value=''><Setter TargetName='Ph' Property='Visibility' Value='Visible'/></Trigger>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource LineStrong}'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'>
              <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Accent}'/>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource Surface}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='Multi' TargetType='TextBox' BasedOn='{StaticResource Input}'>
    <Setter Property='AcceptsReturn' Value='True'/>
    <Setter Property='TextWrapping' Value='Wrap'/>
    <Setter Property='VerticalContentAlignment' Value='Top'/>
    <Setter Property='Padding' Value='9,8'/>
    <Setter Property='MinHeight' Value='60'/>
    <Setter Property='VerticalScrollBarVisibility' Value='Auto'/>
  </Style>

  <!-- ComboBox (works editable and non-editable) -->
  <ControlTemplate x:Key='ComboToggle' TargetType='ToggleButton'>
    <Border x:Name='Bd' Background='{DynamicResource Surface2}' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='8'>
      <TextBlock Text='&#xE70D;' FontFamily='{StaticResource IconFont}' FontSize='10' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,11,0' Foreground='{DynamicResource Text2}'/>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource LineStrong}'/></Trigger>
      <Trigger Property='IsChecked' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>
  <Style TargetType='ComboBoxItem'>
    <Setter Property='Padding' Value='10,7'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBoxItem'>
          <Border x:Name='Bd' CornerRadius='6' Padding='{TemplateBinding Padding}' Background='Transparent'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource Surface2}'/></Trigger>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource AccentSoft}'/>
              <Setter Property='Foreground' Value='{DynamicResource Accent}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='ComboBox'>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='Height' Value='38'/>
    <Setter Property='Padding' Value='11,0,28,0'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='MaxDropDownHeight' Value='290'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBox'>
          <Grid>
            <ToggleButton x:Name='Tg' Template='{StaticResource ComboToggle}' Focusable='False' ClickMode='Press'
                          IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'/>
            <ContentPresenter x:Name='Cp' IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}'
                              ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' Margin='{TemplateBinding Padding}'
                              VerticalAlignment='Center' HorizontalAlignment='Left'/>
            <TextBox x:Name='PART_EditableTextBox' Visibility='Hidden' Margin='9,0,30,0' VerticalAlignment='Center'
                     Background='Transparent' BorderThickness='0' Foreground='{TemplateBinding Foreground}' IsReadOnly='{TemplateBinding IsReadOnly}'>
              <TextBox.Template>
                <ControlTemplate TargetType='TextBox'><ScrollViewer x:Name='PART_ContentHost' Focusable='False'/></ControlTemplate>
              </TextBox.Template>
            </TextBox>
            <Popup x:Name='PART_Popup' Placement='Bottom' VerticalOffset='4' AllowsTransparency='True' Focusable='False'
                   PopupAnimation='Fade' IsOpen='{TemplateBinding IsDropDownOpen}'>
              <Border Background='{DynamicResource Surface}' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='8' Padding='4'
                      MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='{TemplateBinding MaxDropDownHeight}'>
                <ScrollViewer><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsEditable' Value='True'>
              <Setter TargetName='PART_EditableTextBox' Property='Visibility' Value='Visible'/>
              <Setter TargetName='Cp' Property='Visibility' Value='Hidden'/>
              <Setter Property='Cursor' Value='IBeam'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Checkbox: rounded square, green when done -->
  <Style x:Key='Check' TargetType='CheckBox'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <StackPanel Orientation='Horizontal' Background='Transparent'>
            <Border x:Name='Bx' Width='20' Height='20' CornerRadius='6' BorderThickness='2' BorderBrush='{DynamicResource LineStrong}' Background='{DynamicResource Surface}' VerticalAlignment='Top'>
              <Grid>
                <Path x:Name='Ck' Data='M3,8 L6.5,11.5 L13,4.5' Stroke='{DynamicResource OnAccent}' StrokeThickness='2.2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' Visibility='Collapsed'/>
                <Path x:Name='Xk' Data='M4.5,4.5 L11.5,11.5 M11.5,4.5 L4.5,11.5' Stroke='{DynamicResource OnAccent}' StrokeThickness='2.2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' Visibility='Collapsed'/>
              </Grid>
            </Border>
            <ContentPresenter Margin='10,1,0,0' VerticalAlignment='Top'/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bx' Property='BorderBrush' Value='{DynamicResource Ok}'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Bx' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Bx' Property='Background' Value='{DynamicResource Ok}'/>
              <Setter TargetName='Bx' Property='BorderBrush' Value='{DynamicResource Ok}'/>
              <Setter TargetName='Ck' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='{x:Null}'>
              <Setter TargetName='Bx' Property='Background' Value='{DynamicResource Danger}'/>
              <Setter TargetName='Bx' Property='BorderBrush' Value='{DynamicResource Danger}'/>
              <Setter TargetName='Xk' Property='Visibility' Value='Visible'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Small checkbox for checklist steps; the label wraps -->
  <Style x:Key='StepCheck' TargetType='CheckBox'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <Grid Background='Transparent'>
            <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions>
            <Border x:Name='Bx' Width='15' Height='15' CornerRadius='4' BorderThickness='1.5' BorderBrush='{DynamicResource LineStrong}' Background='{DynamicResource Surface}' VerticalAlignment='Top' Margin='0,2,0,0'>
              <Path x:Name='Ck' Data='M2.5,6.5 L5.2,9 L10,3.6' Stroke='{DynamicResource OnAccent}' StrokeThickness='1.8' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' Visibility='Collapsed'/>
            </Border>
            <ContentPresenter Grid.Column='1' Margin='8,0,0,0' VerticalAlignment='Top'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bx' Property='BorderBrush' Value='{DynamicResource Ok}'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Bx' Property='Background' Value='{DynamicResource Ok}'/>
              <Setter TargetName='Bx' Property='BorderBrush' Value='{DynamicResource Ok}'/>
              <Setter TargetName='Ck' Property='Visibility' Value='Visible'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Slim scrollbars -->
  <ControlTemplate x:Key='VScroll' TargetType='ScrollBar'>
    <Grid Background='Transparent' Width='10'>
      <Track x:Name='PART_Track' IsDirectionReversed='True'>
        <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border CornerRadius='3' Background='{DynamicResource Thumb}' Margin='3,2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
      </Track>
    </Grid>
  </ControlTemplate>
  <ControlTemplate x:Key='HScroll' TargetType='ScrollBar'>
    <Grid Background='Transparent' Height='10'>
      <Track x:Name='PART_Track'>
        <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border CornerRadius='3' Background='{DynamicResource Thumb}' Margin='2,3'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
      </Track>
    </Grid>
  </ControlTemplate>
  <Style TargetType='ScrollBar'>
    <Setter Property='Template' Value='{StaticResource VScroll}'/>
    <Setter Property='Width' Value='10'/>
    <Setter Property='MinWidth' Value='10'/>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Template' Value='{StaticResource HScroll}'/>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='MinWidth' Value='0'/>
        <Setter Property='Height' Value='10'/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <!-- Right-click and ... menus: drawn by the app so they follow light / dark (the Windows template is always light) -->
  <Style TargetType='ContextMenu'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='FontFamily' Value='Segoe UI'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ContextMenu'>
          <Border Background='{DynamicResource Surface}' BorderBrush='{DynamicResource LineStrong}' BorderThickness='1' CornerRadius='8' Padding='4' Margin='0,0,10,10'>
            <Border.Effect><DropShadowEffect BlurRadius='12' ShadowDepth='3' Direction='270' Opacity='0.22'/></Border.Effect>
            <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='Separator'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Separator'><Border Height='1' Margin='8,4' Background='{DynamicResource Line}'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='MenuItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <Border x:Name='Bd' Background='Transparent' CornerRadius='5' Padding='6,6,10,6' MinWidth='200'>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='22'/>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='Auto'/>
              </Grid.ColumnDefinitions>
              <ContentPresenter x:Name='Ico' ContentSource='Icon' VerticalAlignment='Center' HorizontalAlignment='Center'/>
              <TextBlock x:Name='Chk' Text='&#xE73E;' FontFamily='{StaticResource IconFont}' FontSize='12' Foreground='{DynamicResource Accent}'
                         VerticalAlignment='Center' HorizontalAlignment='Center' Visibility='Collapsed'/>
              <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center' Margin='6,0,0,0'/>
              <TextBlock x:Name='Chk2' Grid.Column='2' Text='&#xE73E;' FontFamily='{StaticResource IconFont}' FontSize='12' Foreground='{DynamicResource Accent}'
                         VerticalAlignment='Center' Margin='14,0,0,0' Visibility='Collapsed'/>
              <TextBlock x:Name='Arrow' Grid.Column='2' Text='&#xE76C;' FontFamily='{StaticResource IconFont}' FontSize='10'
                         VerticalAlignment='Center' Margin='14,0,0,0' Visibility='Collapsed'/>
              <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='2' VerticalOffset='-5' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'
                     IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'>
                <Border Background='{DynamicResource Surface}' BorderBrush='{DynamicResource LineStrong}' BorderThickness='1' CornerRadius='8' Padding='4' Margin='0,0,10,10'>
                  <Border.Effect><DropShadowEffect BlurRadius='12' ShadowDepth='3' Direction='270' Opacity='0.22'/></Border.Effect>
                  <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
                </Border>
              </Popup>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource AccentSoft}'/></Trigger>
            <Trigger Property='IsChecked' Value='True'><Setter TargetName='Chk2' Property='Visibility' Value='Visible'/></Trigger>
            <MultiTrigger>
              <MultiTrigger.Conditions><Condition Property='IsChecked' Value='True'/><Condition Property='Icon' Value='{x:Null}'/></MultiTrigger.Conditions>
              <Setter TargetName='Chk' Property='Visibility' Value='Visible'/>
              <Setter TargetName='Chk2' Property='Visibility' Value='Collapsed'/>
            </MultiTrigger>
            <Trigger Property='Role' Value='SubmenuHeader'><Setter TargetName='Arrow' Property='Visibility' Value='Visible'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Foreground' Value='{DynamicResource Text3}'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='Background' Value='{DynamicResource Tip}'/>
    <Setter Property='Foreground' Value='White'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='Padding' Value='8,4'/>
  </Style>
</ResourceDictionary>";
    }
}

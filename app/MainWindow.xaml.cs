using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ScyllaConfigurator;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<QuickKey> _quickKeys = new();
    private readonly ObservableCollection<SavedCombo> _savedCombos = new();
    private readonly Dictionary<int, ObservableCollection<MacroAction>> _macroSlots = new();
    private readonly HashSet<Key> _macroHeldKeys = new();
    private readonly Stack<KeyChange> _undo = new();
    private readonly Dictionary<(int Row, int Col), Button> _buttons = new();
    private readonly Dictionary<(int Layer, int Row, int Col), ushort> _keymap = new();
    private List<LayoutKey> _layout = new();
    private VialClient? _client;
    private LayoutKey? _selected;
    private int _layer;
    private bool _loading;
    private bool _capturePhysicalKey;
    private bool _recordMacro;
    private int _activeMacroSlot;
    private ObservableCollection<MacroAction> _activeMacroSteps = new();

    private static string ComboStorePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScyllaConfigurator", "custom-combos.json");

    private static readonly byte[] SupportedVialUid =
        [0x5B, 0x76, 0x3F, 0xFF, 0xA8, 0x70, 0x33, 0xC8];

    private static readonly (int Row, int Col)[] MatrixOrder =
    [
        (0,0),(0,1),(0,2),(0,3),(0,4),(0,5), (5,5),(5,4),(5,3),(5,2),(5,1),(5,0),
        (1,0),(1,1),(1,2),(1,3),(1,4),(1,5), (6,5),(6,4),(6,3),(6,2),(6,1),(6,0),
        (2,0),(2,1),(2,2),(2,3),(2,4),(2,5), (7,5),(7,4),(7,3),(7,2),(7,1),(7,0),
        (3,0),(3,1),(3,2),(3,3),(3,4),(3,5), (8,5),(8,4),(8,3),(8,2),(8,1),(8,0),
        (4,3),(4,4),(4,1),(9,1),(9,4),(9,3),(4,5),(4,2),(9,2),(9,5)
    ];

    public MainWindow()
    {
        InitializeComponent();
        LoadQuickKeys();
        LoadSavedCombos();
        LoadLayout();
        LoadDefaultKeymap();
        BuildKeyCanvas();
        RefreshButtons();
        QuickKeyCombo.ItemsSource = _quickKeys;
        QuickKeyCombo.SelectedIndex = 0;
        ComboMainKeyCombo.ItemsSource = _quickKeys.Where(x => x.Keycode < 0xE0 && x.Keycode != 0 && x.Keycode != 1).ToList();
        ComboMainKeyCombo.SelectedIndex = 0;
        SavedComboList.ItemsSource = _savedCombos;
        for (var slot = 0; slot < 16; slot++) MacroSlotCombo.Items.Add($"매크로 {slot + 1}");
        MacroActionTypeCombo.ItemsSource = new[] { "탭", "누름", "뗌", "지연" };
        MacroActionTypeCombo.SelectedIndex = 0;
        MacroActionKeyCombo.ItemsSource = _quickKeys.Where(x => x.Keycode != 0 && x.Keycode != 1).ToList();
        MacroActionKeyCombo.SelectedIndex = 0;
        MacroStepsList.ItemsSource = _activeMacroSteps;
        MacroSlotCombo.SelectedIndex = 0;
        UpdateComboPreview();
        SetLayerButtonState();
        Loaded += async (_, _) => await ConnectAsync();
    }

    private void LoadQuickKeys()
    {
        var keys = new[]
        {
            ("Esc",0x29),("Tab",0x2B),("Enter",0x28),("Backspace",0x2A),("Space",0x2C),
            ("A",0x04),("B",0x05),("C",0x06),("D",0x07),("E",0x08),("F",0x09),("G",0x0A),("H",0x0B),
            ("I",0x0C),("J",0x0D),("K",0x0E),("L",0x0F),("M",0x10),("N",0x11),("O",0x12),("P",0x13),
            ("Q",0x14),("R",0x15),("S",0x16),("T",0x17),("U",0x18),("V",0x19),("W",0x1A),("X",0x1B),
            ("Y",0x1C),("Z",0x1D),("0",0x27),("1",0x1E),("2",0x1F),("3",0x20),("4",0x21),("5",0x22),
            ("6",0x23),("7",0x24),("8",0x25),("9",0x26),("↑",0x52),("↓",0x51),("←",0x50),("→",0x4F),
            ("Ctrl",0xE0),("Shift",0xE1),("Alt",0xE2),("GUI",0xE3),("KC_TRNS",0x0001),("KC_NO",0x0000)
        };
        foreach (var (label, code) in keys) _quickKeys.Add(new QuickKey(label, (ushort)code));
        _quickKeys.Add(new QuickKey("Insert", 0x49));
        _quickKeys.Add(new QuickKey("Delete", 0x4C));
        _quickKeys.Add(new QuickKey("Home", 0x4A));
        _quickKeys.Add(new QuickKey("End", 0x4D));
        _quickKeys.Add(new QuickKey("Page Up", 0x4B));
        _quickKeys.Add(new QuickKey("Page Down", 0x4E));
        for (var i = 0; i < 12; i++) _quickKeys.Add(new QuickKey($"F{i + 1}", (ushort)(0x3A + i)));
    }

    private void LoadSavedCombos()
    {
        try
        {
            if (!File.Exists(ComboStorePath)) return;
            var saved = JsonSerializer.Deserialize<List<SavedCombo>>(File.ReadAllText(ComboStorePath));
            if (saved is not null)
                foreach (var combo in saved.Where(x => !string.IsNullOrWhiteSpace(x.Name))) _savedCombos.Add(combo);
        }
        catch
        {
            // A damaged app-local list should not prevent the keyboard editor from opening.
        }
    }

    private void SaveSavedCombos()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ComboStorePath)!);
        File.WriteAllText(ComboStorePath, JsonSerializer.Serialize(_savedCombos, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void LoadLayout()
    {
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("ScyllaConfigurator.ScyllaLayout.json")
            ?? throw new InvalidOperationException("Scylla 레이아웃 리소스를 찾을 수 없습니다.");
        _layout = JsonSerializer.Deserialize<List<LayoutKey>>(stream) ?? new();
    }

    private void LoadDefaultKeymap()
    {
        var layer0 = new ushort[]
        {
            0x29,0x1E,0x1F,0x20,0x21,0x22, 0x23,0x24,0x25,0x26,0x27,0x2A,
            0x2B,0x14,0x1A,0x08,0x15,0x17, 0x1C,0x18,0x0C,0x12,0x13,0x2D,
            0xE1,0x04,0x16,0x07,0x09,0x0A, 0x0B,0x0D,0x0E,0x0F,0x33,0x34,
            0xE0,0x1D,0x1B,0x06,0x19,0x05, 0x11,0x10,0x36,0x37,0x38,0x31,
            0xE0,0x2C,0x5221,0x5222,0x28,0xE7,0x4A,0x2A,0x4C,0xE4
        };
        for (var i = 0; i < MatrixOrder.Length; i++) _keymap[(0, MatrixOrder[i].Row, MatrixOrder[i].Col)] = layer0[i];
        for (var layer = 1; layer < 4; layer++)
            foreach (var key in MatrixOrder) _keymap[(layer, key.Row, key.Col)] = 0x0001;
        _keymap[(3, 0, 0)] = 0x5200;
    }

    private void BuildKeyCanvas()
    {
        foreach (var key in _layout)
        {
            var button = new Button { Width = 46, Height = 46, Style = (Style)FindResource("KeyButton"), Tag = key, ToolTip = $"matrix ({key.Row},{key.Col})" };
            button.Click += KeyButton_Click;
            Canvas.SetLeft(button, key.X); Canvas.SetTop(button, key.Y);
            KeyCanvas.Children.Add(button);
            _buttons[(key.Row, key.Col)] = button;
        }
        var title = new TextBlock { Text = "LEFT", Foreground = (Brush)FindResource("MutedTextBrush"), FontWeight = FontWeights.Bold };
        Canvas.SetLeft(title, 130); Canvas.SetTop(title, 370); KeyCanvas.Children.Add(title);
        var right = new TextBlock { Text = "RIGHT", Foreground = (Brush)FindResource("MutedTextBrush"), FontWeight = FontWeights.Bold };
        Canvas.SetLeft(right, 600); Canvas.SetTop(right, 370); KeyCanvas.Children.Add(right);
    }

    private async Task ConnectAsync()
    {
        Disconnect();
        try
        {
            var devices = HidDevice.FindRaw()
                .OrderByDescending(d => d.InputReportLength == 32 || d.InputReportLength == 33)
                .ToArray();
            if (devices.Length == 0) { SetDisconnected("Raw HID 장치를 찾지 못했습니다."); return; }

            var sawVialDevice = false;
            var sawDifferentVialDevice = false;
            foreach (var info in devices)
            {
                HidDevice? device = null;
                try
                {
                    device = HidDevice.Open(info);
                    var client = new VialClient(device);
                    var id = await client.GetKeyboardIdAsync();
                    sawVialDevice = true;
                    if (!id.Uid.SequenceEqual(SupportedVialUid))
                    {
                        sawDifferentVialDevice = true;
                        client.Dispose();
                        device = null;
                        continue;
                    }
                    _client = client;
                    device = null; // owned by VialClient now
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(93, 215, 137));
                    StatusText.Text = "Scylla 연결됨";
                    DeviceInfoText.Text = $"Vial protocol 0x{id.Version:X8}\nVID:PID {info.VendorId:X4}:{info.ProductId:X4}\nUID: {Convert.ToHexString(id.Uid)}";
                    FooterText.Text = "연결됨 · 변경 후 ‘키보드에 저장’을 누르세요.";
                    await LoadLayerAsync(_layer);
                    return;
                }
                catch
                {
                    device?.Dispose();
                }
            }

            SetDisconnected(sawDifferentVialDevice
                ? "Vial 장치는 찾았지만 Scylla용 UID가 아닙니다. 지원 펌웨어를 확인하세요."
                : sawVialDevice
                    ? "Vial 장치는 응답했지만 지원되는 Scylla 장치가 아닙니다."
                    : $"Raw HID {devices.Length}개를 찾았지만 Vial 응답이 없습니다. vendor_novbus는 지원되지 않습니다.");
        }
        catch (Exception ex) { Disconnect(); SetDisconnected("연결 실패: " + ex.Message); }
    }

    private void SetDisconnected(string message)
    {
        StatusDot.Fill = new SolidColorBrush(Color.FromRgb(229, 107, 111));
        StatusText.Text = "키보드 연결 안 됨";
        DeviceInfoText.Text = message;
        FooterText.Text = "USB는 오른쪽 master에 연결하세요.";
        RefreshButtons();
    }

    private void Disconnect() { _client?.Dispose(); _client = null; }

    private async Task LoadLayerAsync(int layer)
    {
        if (_loading) return;
        if (_client is null)
        {
            RefreshButtons();
            FooterText.Text = $"Layer {layer} · 키보드가 연결되면 실제 키맵을 읽습니다.";
            return;
        }
        _loading = true;
        try
        {
            FooterText.Text = $"Layer {layer} 읽는 중…";
            foreach (var key in _layout)
            {
                key.Keycode = await _client.GetKeycodeAsync(layer, key.Row, key.Col);
                _keymap[(layer, key.Row, key.Col)] = key.Keycode;
            }
            RefreshButtons();
            FooterText.Text = $"Layer {layer} 로드 완료";
        }
        catch (Exception ex) { FooterText.Text = "읽기 실패: " + ex.Message; }
        finally { _loading = false; }
    }

    private void RefreshButtons()
    {
        foreach (var key in _layout)
        {
            var code = _keymap.GetValueOrDefault((_layer, key.Row, key.Col));
            if (_buttons.TryGetValue((key.Row, key.Col), out var button))
            {
                button.Content = KeyLabel(code);
                button.Background = _capturePhysicalKey
                    ? (_selected == key ? new SolidColorBrush(Color.FromRgb(211, 104, 30)) : new SolidColorBrush(Color.FromRgb(239, 145, 61)))
                    : (_selected == key ? new SolidColorBrush(Color.FromRgb(71, 142, 190)) : new SolidColorBrush(Color.FromRgb(38, 52, 67)));
            }
        }
        if (_selected is not null) ShowSelected(_selected);
        UndoButton.IsEnabled = _undo.Count > 0;
        UpdateCaptureModeVisual();
    }

    private static string KeyLabel(ushort keycode)
    {
        if (keycode == 0) return "NO";
        if (keycode == 1) return "TRNS";
        if (keycode >= 0x04 && keycode <= 0x1D) return ((char)('A' + keycode - 0x04)).ToString();
        if (keycode >= 0x1E && keycode <= 0x27) return ((keycode - 0x1E + 1) % 10).ToString();
        return keycode switch
        {
            0x28 => "ENTER", 0x29 => "ESC", 0x2A => "BSPC", 0x2B => "TAB", 0x2C => "SPACE",
            0x2D => "-", 0x31 => "\\", 0x33 => ";", 0x34 => "'", 0x36 => ",", 0x37 => ".", 0x38 => "/",
            0x4A => "HOME", 0x4C => "DEL", 0x4F => "→", 0x50 => "←", 0x51 => "↓", 0x52 => "↑",
            0xE0 => "LCTL", 0xE1 => "LSFT", 0xE2 => "LALT", 0xE3 => "LGUI", 0xE4 => "RALT", 0xE5 => "RSFT", 0xE6 => "RCTL", 0xE7 => "RGUI",
            0x5221 => "MO(1)", 0x5222 => "MO(2)", 0x5200 => "TO(0)", _ => $"0x{keycode:X4}"
        };
    }

    private void KeyButton_Click(object sender, RoutedEventArgs e)
    {
        _selected = (sender as Button)?.Tag as LayoutKey;
        if (_selected is not null) ShowSelected(_selected);
        RefreshButtons();
    }

    private void ShowSelected(LayoutKey key)
    {
        var code = _keymap.GetValueOrDefault((_layer, key.Row, key.Col));
        SelectedKeyText.Text = $"{key.Label}  ·  ({key.Row},{key.Col})";
        KeycodeTextBox.Text = $"0x{code:X4}";
        QuickKeyCombo.SelectedItem = _quickKeys.FirstOrDefault(x => x.Keycode == code);
    }

    private async void ApplyKey_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        ushort code;
        if (!ushort.TryParse(KeycodeTextBox.Text.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase), System.Globalization.NumberStyles.HexNumber, null, out code))
        {
            if (QuickKeyCombo.SelectedItem is not QuickKey quick) { MessageBox.Show("키코드를 0x0000 형식으로 입력하세요.", "입력 확인"); return; }
            code = quick.Keycode;
        }
        await ApplySelectedKeyAsync(code);
    }

    private async Task ApplySelectedKeyAsync(ushort code)
    {
        if (_selected is null) return;
        var oldCode = _keymap.GetValueOrDefault((_layer, _selected.Row, _selected.Col));
        if (oldCode == code) return;
        _undo.Push(new KeyChange(_layer, _selected.Row, _selected.Col, oldCode, code));
        _keymap[(_layer, _selected.Row, _selected.Col)] = code;
        _selected.Keycode = code;
        RefreshButtons();
        FooterText.Text = "변경 대기 중 · 저장하려면 ‘키보드에 저장’을 누르세요.";
        if (AutoSaveCheckBox.IsChecked == true && _client is not null)
        {
            try
            {
                await _client.SetKeycodeAndVerifyAsync(_layer, _selected.Row, _selected.Col, code);
                FooterText.Text = $"{KeyLabel(code)} 저장 완료";
            }
            catch (Exception ex)
            {
                _keymap[(_layer, _selected.Row, _selected.Col)] = oldCode;
                _selected.Keycode = oldCode;
                _undo.Pop();
                RefreshButtons();
                FooterText.Text = "자동 저장 실패: " + ex.Message;
            }
        }
    }

    private void CaptureKey_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            MessageBox.Show("먼저 화면에서 지정할 키를 선택하세요.", "키 선택");
            return;
        }
        _capturePhysicalKey = !_capturePhysicalKey;
        RefreshButtons();
        if (!_capturePhysicalKey)
        {
            FooterText.Text = "실제 키 입력 모드를 종료했습니다.";
            return;
        }
        Activate();
        Focus();
        FooterText.Text = "지정할 키를 실제 키보드에서 한 번 누르세요. 버튼을 다시 누르면 종료됩니다.";
    }

    private void UpdateCaptureModeVisual()
    {
        CaptureButton.Content = _capturePhysicalKey ? "키 입력 모드 종료" : "키 입력 모드 시작";
        CaptureButton.Background = _capturePhysicalKey
            ? new SolidColorBrush(Color.FromRgb(239, 145, 61))
            : (Brush)FindResource("PanelBrush2");
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_recordMacro)
        {
            var recordedKey = e.Key == Key.System ? e.SystemKey : e.Key;
            if (TryGetQmkKeycode(recordedKey, out var recordedCode) && _macroHeldKeys.Add(recordedKey))
            {
                _activeMacroSteps.Add(new MacroAction { Type = MacroActionType.Down, Keycode = recordedCode });
                MacroStepsList.SelectedIndex = _activeMacroSteps.Count - 1;
                e.Handled = true;
            }
            return;
        }
        if (!_capturePhysicalKey) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (TryGetQmkKeycode(key, out var code))
        {
            KeycodeTextBox.Text = $"0x{code:X4}";
            await ApplySelectedKeyAsync(code);
            FooterText.Text = $"{KeyLabel(code)} 지정 완료 · 입력 모드 유지 중입니다. 다른 화면 키를 선택해 계속 지정하세요.";
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_recordMacro) return;
        var recordedKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (TryGetQmkKeycode(recordedKey, out var recordedCode) && _macroHeldKeys.Remove(recordedKey))
        {
            _activeMacroSteps.Add(new MacroAction { Type = MacroActionType.Up, Keycode = recordedCode });
            MacroStepsList.SelectedIndex = _activeMacroSteps.Count - 1;
            e.Handled = true;
        }
    }

    private static bool TryGetQmkKeycode(Key key, out ushort code)
    {
        code = 0;
        if (key >= Key.A && key <= Key.Z) { code = (ushort)(0x04 + key - Key.A); return true; }
        if (key >= Key.D1 && key <= Key.D9) { code = (ushort)(0x1E + key - Key.D1); return true; }
        if (key == Key.D0) { code = 0x27; return true; }
        if (key >= Key.NumPad1 && key <= Key.NumPad9) { code = (ushort)(0x59 + key - Key.NumPad1); return true; }
        if (key == Key.NumPad0) { code = 0x62; return true; }
        if (key >= Key.F1 && key <= Key.F12) { code = (ushort)(0x3A + key - Key.F1); return true; }
        code = key switch
        {
            Key.Escape => 0x29, Key.Tab => 0x2B, Key.CapsLock => 0x39, Key.LeftShift => 0xE1, Key.RightShift => 0xE5,
            Key.LeftCtrl => 0xE0, Key.RightCtrl => 0xE4, Key.LeftAlt => 0xE2, Key.RightAlt => 0xE6,
            Key.LWin => 0xE3, Key.RWin => 0xE7, Key.Space => 0x2C, Key.Enter => 0x28, Key.Back => 0x2A,
            Key.Insert => 0x49, Key.Delete => 0x4C, Key.Home => 0x4A, Key.End => 0x4D, Key.PageUp => 0x4B, Key.PageDown => 0x4E,
            Key.Left => 0x50, Key.Down => 0x51, Key.Up => 0x52, Key.Right => 0x4F,
            Key.OemMinus => 0x2D, Key.OemPlus => 0x2E, Key.OemOpenBrackets => 0x2F, Key.OemCloseBrackets => 0x30,
            Key.OemPipe => 0x31, Key.OemSemicolon => 0x33, Key.OemQuotes => 0x34, Key.OemComma => 0x36,
            Key.OemPeriod => 0x37, Key.OemQuestion => 0x38, Key.OemTilde => 0x35, _ => 0
        };
        return code != 0;
    }

    private void ComboPart_Changed(object sender, RoutedEventArgs e) => UpdateComboPreview();

    private void UpdateComboPreview()
    {
        var description = BuildComboDescription();
        var code = BuildComboKeycode();
        NewComboPreviewText.Text = $"미리 보기: {description} · 0x{code:X4}";
    }

    private string BuildComboDescription()
    {
        var parts = new List<string>();
        if (ComboCtrlCheckBox.IsChecked == true) parts.Add("Ctrl");
        if (ComboShiftCheckBox.IsChecked == true) parts.Add("Shift");
        if (ComboAltCheckBox.IsChecked == true) parts.Add("Alt");
        if (ComboWinCheckBox.IsChecked == true) parts.Add("Win");
        if (ComboMainKeyCombo.SelectedItem is QuickKey key) parts.Add(key.Label);
        return parts.Count == 0 ? "조합키" : string.Join(" + ", parts);
    }

    private ushort BuildComboKeycode()
    {
        var mods = 0;
        if (ComboCtrlCheckBox.IsChecked == true) mods |= 0x01;
        if (ComboShiftCheckBox.IsChecked == true) mods |= 0x02;
        if (ComboAltCheckBox.IsChecked == true) mods |= 0x04;
        if (ComboWinCheckBox.IsChecked == true) mods |= 0x08;
        return (ushort)((mods << 8) | ((ComboMainKeyCombo.SelectedItem as QuickKey)?.Keycode ?? 0));
    }

    private void SavedComboList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedComboList.SelectedItem is SavedCombo combo)
        {
            SavedComboPreviewText.Text = $"선택됨: {combo.Description} · 0x{combo.Keycode:X4}";
        }
    }

    private void SaveCustomCombo_Click(object sender, RoutedEventArgs e)
    {
        var name = ComboNameTextBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show("저장할 조합키 이름을 입력하세요.", "입력 확인");
            return;
        }
        if (ComboMainKeyCombo.SelectedItem is not QuickKey)
        {
            MessageBox.Show("마지막 키를 선택하세요.", "입력 확인");
            return;
        }

        var combo = new SavedCombo { Name = name, Description = BuildComboDescription(), Keycode = BuildComboKeycode() };
        var oldIndex = _savedCombos.ToList().FindIndex(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (oldIndex >= 0) _savedCombos[oldIndex] = combo;
        else _savedCombos.Add(combo);
        SavedComboList.SelectedItem = combo;
        try
        {
            SaveSavedCombos();
            FooterText.Text = $"‘{name}’ 조합키를 앱에 저장했습니다.";
        }
        catch (Exception ex) { FooterText.Text = "앱 저장 실패: " + ex.Message; }
    }

    private async void ApplySavedCombo_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            MessageBox.Show("먼저 화면에서 적용할 키를 선택하세요.", "키 선택");
            return;
        }
        if (SavedComboList.SelectedItem is not SavedCombo combo)
        {
            MessageBox.Show("먼저 저장된 조합키를 선택하세요.", "조합키 선택");
            return;
        }
        KeycodeTextBox.Text = $"0x{combo.Keycode:X4}";
        await ApplySelectedKeyAsync(combo.Keycode);
    }

    private void DeleteCustomCombo_Click(object sender, RoutedEventArgs e)
    {
        if (SavedComboList.SelectedItem is not SavedCombo combo) return;
        _savedCombos.Remove(combo);
        try
        {
            SaveSavedCombos();
            FooterText.Text = $"‘{combo.Name}’ 조합키를 삭제했습니다.";
        }
        catch (Exception ex) { FooterText.Text = "앱 저장 실패: " + ex.Message; }
    }

    private async void ReadMacroButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null) { MessageBox.Show("먼저 키보드를 연결하세요."); return; }
        try
        {
            var slot = MacroSlotCombo.SelectedIndex;
            var buffer = await _client.GetMacroBufferAsync();
            _activeMacroSteps.Clear();
            foreach (var action in MacroCodec.DecodeActions(buffer, slot)) _activeMacroSteps.Add(action);
            MacroStepsList.SelectedIndex = -1;
            FooterText.Text = $"매크로 {slot + 1} 읽기 완료";
        }
        catch (Exception ex) { FooterText.Text = "매크로 읽기 실패: " + ex.Message; }
    }

    private async void SaveMacroButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null) { MessageBox.Show("먼저 키보드를 연결하세요."); return; }
        try
        {
            if (!await EnsureVialUnlockedAsync()) return;
            var bytes = MacroCodec.EncodeActions(_activeMacroSteps);
            var slot = MacroSlotCombo.SelectedIndex;
            await _client.SaveMacroAsync(slot, bytes);

            if (_selected is null)
            {
                FooterText.Text = $"매크로 {slot + 1} 저장 완료 · 지정할 화면 키를 선택하세요.";
                return;
            }

            var macroCode = (ushort)(0x7700 + slot);
            var oldCode = _keymap.GetValueOrDefault((_layer, _selected.Row, _selected.Col));
            await _client.SetKeycodeAsync(_layer, _selected.Row, _selected.Col, macroCode);
            var savedCode = await _client.GetKeycodeAsync(_layer, _selected.Row, _selected.Col);
            if (savedCode != macroCode) throw new InvalidOperationException("선택 키에 매크로가 반영되지 않았습니다.");

            if (oldCode != macroCode) _undo.Push(new KeyChange(_layer, _selected.Row, _selected.Col, oldCode, macroCode));
            _keymap[(_layer, _selected.Row, _selected.Col)] = macroCode;
            _selected.Keycode = macroCode;
            RefreshButtons();
            FooterText.Text = $"매크로 {slot + 1} 저장 및 {SelectedKeyText.Text} 지정 완료";
        }
        catch (Exception ex) { FooterText.Text = "매크로 저장 실패: " + ex.Message; }
    }

    private async Task<bool> EnsureVialUnlockedAsync()
    {
        if (_client is null) return false;
        var status = await _client.GetUnlockStatusAsync();
        if (status.Unlocked) return true;

        var answer = MessageBox.Show(
            "매크로를 저장하려면 Vial 잠금 해제가 필요합니다.\n\n왼쪽 Esc와 오른쪽 Backspace를 동시에 누른 채 확인을 누르고, 완료될 때까지 계속 누르세요.",
            "Vial 잠금 해제",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);
        if (answer != MessageBoxResult.OK) return false;

        FooterText.Text = "Esc + Backspace를 누른 상태로 유지하세요…";
        var unlocked = await _client.UnlockAsync();
        if (!unlocked) FooterText.Text = "잠금 해제 실패 · 두 키를 끝까지 눌렀는지 확인하세요.";
        return unlocked;
    }

    private void MacroSlotCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var slot = MacroSlotCombo.SelectedIndex;
        if (slot < 0 || slot == _activeMacroSlot && _macroSlots.ContainsKey(slot)) return;
        _macroSlots[_activeMacroSlot] = _activeMacroSteps;
        _activeMacroSlot = slot;
        if (_macroSlots.TryGetValue(slot, out var existing))
        {
            _activeMacroSteps = existing;
        }
        else
        {
            _activeMacroSteps = new ObservableCollection<MacroAction>();
            _macroSlots[slot] = _activeMacroSteps;
        }
        MacroStepsList.ItemsSource = _activeMacroSteps;
        MacroStepsList.SelectedIndex = -1;
    }

    private void AddMacroActionButton_Click(object sender, RoutedEventArgs e)
    {
        var type = MacroActionTypeCombo.SelectedIndex switch
        {
            0 => MacroActionType.Tap,
            1 => MacroActionType.Down,
            2 => MacroActionType.Up,
            3 => MacroActionType.Delay,
            _ => MacroActionType.Tap
        };
        var action = new MacroAction { Type = type };
        if (type == MacroActionType.Delay)
        {
            if (!int.TryParse(MacroDelayTextBox.Text.Trim(), out var delay) || delay < 1 || delay > 65025)
            {
                MessageBox.Show("지연 시간은 1~65025 ms 사이의 숫자로 입력하세요.", "입력 확인");
                return;
            }
            action.DelayMs = delay;
        }
        else if (MacroActionKeyCombo.SelectedItem is QuickKey key)
        {
            action.Keycode = key.Keycode;
        }
        else
        {
            MessageBox.Show("동작에 사용할 키를 선택하세요.", "키 선택");
            return;
        }
        _activeMacroSteps.Add(action);
        MacroStepsList.SelectedIndex = _activeMacroSteps.Count - 1;
        FooterText.Text = "매크로 동작을 추가했습니다. 저장하려면 ‘매크로를 키보드에 저장’을 누르세요.";
    }

    private void DeleteMacroActionButton_Click(object sender, RoutedEventArgs e)
    {
        var index = MacroStepsList.SelectedIndex;
        if (index < 0 || index >= _activeMacroSteps.Count) return;
        _activeMacroSteps.RemoveAt(index);
        MacroStepsList.SelectedIndex = Math.Min(index, _activeMacroSteps.Count - 1);
    }

    private void MoveMacroActionButton_Click(object sender, RoutedEventArgs e)
    {
        var index = MacroStepsList.SelectedIndex;
        if (index < 0) return;
        var target = (sender as Button)?.Tag?.ToString() == "down" ? index + 1 : index - 1;
        if (target < 0 || target >= _activeMacroSteps.Count) return;
        (_activeMacroSteps[index], _activeMacroSteps[target]) = (_activeMacroSteps[target], _activeMacroSteps[index]);
        MacroStepsList.Items.Refresh();
        MacroStepsList.SelectedIndex = target;
    }

    private void RecordMacroButton_Click(object sender, RoutedEventArgs e)
    {
        _recordMacro = !_recordMacro;
        if (!_recordMacro)
        {
            foreach (var key in _macroHeldKeys)
                if (TryGetQmkKeycode(key, out var code)) _activeMacroSteps.Add(new MacroAction { Type = MacroActionType.Up, Keycode = code });
            _macroHeldKeys.Clear();
        }
        RecordMacroButton.Content = _recordMacro ? "녹화 중지" : "녹화 시작";
        RecordMacroButton.Background = _recordMacro
            ? new SolidColorBrush(Color.FromRgb(211, 104, 30))
            : (Brush)FindResource("PanelBrush2");
        FooterText.Text = _recordMacro ? "녹화 중… 키보드로 순서를 입력한 뒤 ‘녹화 중지’를 누르세요." : "매크로 녹화를 중지했습니다.";
    }

    private async void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0) return;
        var change = _undo.Pop();
        _keymap[(change.Layer, change.Row, change.Col)] = change.Before;
        RefreshButtons();
        try
        {
            if (_client is not null)
                await _client.SetKeycodeAndVerifyAsync(change.Layer, change.Row, change.Col, change.Before);
            FooterText.Text = $"Layer {change.Layer} ({change.Row},{change.Col}) 변경을 되돌렸습니다.";
        }
        catch (Exception ex)
        {
            _keymap[(change.Layer, change.Row, change.Col)] = change.After;
            _undo.Push(change);
            RefreshButtons();
            FooterText.Text = "되돌리기 실패: " + ex.Message;
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null) { MessageBox.Show("먼저 키보드를 연결하세요."); return; }
        try
        {
            foreach (var key in _layout)
                await _client.SetKeycodeAndVerifyAsync(_layer, key.Row, key.Col, _keymap.GetValueOrDefault((_layer, key.Row, key.Col)));
            FooterText.Text = $"Layer {_layer} 저장 완료";
        }
        catch (Exception ex) { FooterText.Text = "저장 실패: " + ex.Message; }
    }

    private async void ReadButton_Click(object sender, RoutedEventArgs e) => await LoadLayerAsync(_layer);
    private async void ConnectButton_Click(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null) { MessageBox.Show("먼저 키보드를 연결하세요."); return; }
        try
        {
            var ok = await EnsureVialUnlockedAsync();
            if (ok) FooterText.Text = "Vial 잠금 해제 완료";
        }
        catch (Exception ex) { FooterText.Text = "잠금 해제 실패: " + ex.Message; }
    }

    private async void LayerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || !int.TryParse(b.Tag?.ToString(), out var layer)) return;
        _layer = layer; SetLayerButtonState(); await LoadLayerAsync(_layer);
    }

    private void SetLayerButtonState()
    {
        var buttons = new[] { Layer0Button, Layer1Button, Layer2Button, Layer3Button };
        foreach (var button in buttons) button.Background = (int.Parse(button.Tag.ToString()!) == _layer) ? new SolidColorBrush(Color.FromRgb(71, 142, 190)) : (Brush)FindResource("PanelBrush2");
    }

    private void Window_Closed(object? sender, EventArgs e) => Disconnect();
}

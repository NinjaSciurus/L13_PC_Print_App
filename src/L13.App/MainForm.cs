using System.Drawing;
using L13.Core.Printing;
using L13.Core.Rendering;

namespace L13.App;

/// <summary>
/// Single-window utility: configure a label, see a live preview, and print it
/// over Bluetooth with a progress bar. UI is built in code (no designer) so the
/// whole app is a handful of readable files.
/// </summary>
public sealed class MainForm : Form
{
    private readonly L13LabelPrinter _printer = new();
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 250 };
    private CancellationTokenSource? _cts;
    private string? _imagePath;
    private SplitContainer _split = null!;

    // Inputs
    private readonly TextBox _txtText = new() { Multiline = true, Text = "HELLO", ScrollBars = ScrollBars.Vertical };
    private readonly ComboBox _cmbFont = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _chkBold = new() { Text = "Bold", Checked = true };
    private readonly NumericUpDown _numSize = new() { Minimum = 0, Maximum = 400, Value = 0 };
    private readonly NumericUpDown _numMinFont = new() { Minimum = 4, Maximum = 96, Value = 12 };
    private readonly ComboBox _cmbRotate = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _chkFlip = new() { Text = "Mirror (flip)" };
    private readonly NumericUpDown _numLen = new() { Minimum = 5, Maximum = 100, DecimalPlaces = 1, Increment = 0.5m, Value = 28 };
    private readonly NumericUpDown _numEject = new() { Minimum = 0, Maximum = 30, DecimalPlaces = 1, Increment = 0.5m, Value = 6 };
    private readonly CheckBox _chkBorder = new() { Text = "Border" };
    private readonly NumericUpDown _numBorderW = new() { Minimum = 1, Maximum = 10, Value = 2 };
    private readonly ComboBox _cmbDensity = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _txtImage = new() { ReadOnly = true, PlaceholderText = "(text mode)" };
    private readonly Button _btnBrowse = new() { Text = "Image…", Width = 80 };
    private readonly Button _btnClearImg = new() { Text = "Clear", Width = 60 };
    private readonly TextBox _txtAddr = new() { Text = "55:55:09:22:3F:9B" };

    // Output / actions
    // Light-gray surround so the white printable-area image reads as the label boundary.
    private readonly PictureBox _picPreview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(220, 220, 220), BorderStyle = BorderStyle.FixedSingle };
    private readonly ProgressBar _prog = new() { Dock = DockStyle.Top, Height = 18, Minimum = 0, Maximum = 100 };
    private readonly Label _lblStatus = new() { Dock = DockStyle.Fill, Text = "Ready.", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) };
    private readonly Button _btnPrint = new() { Text = "Print", Width = 90 };
    private readonly Button _btnCancel = new() { Text = "Cancel", Width = 90, Enabled = false };
    private readonly Button _btnInfo = new() { Text = "Printer info", Width = 110 };

    public MainForm()
    {
        Text = "L13 Label Printer";
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("app.ico"))
        {
            if (iconStream is not null)
                Icon = new Icon(iconStream);
        }
        // Tall enough to show the whole left-hand settings column at 100% DPI
        // (the panel still scrolls if a higher DPI scales the controls up).
        MinimumSize = new Size(960, 620);
        Size = new Size(960, 740);
        StartPosition = FormStartPosition.CenterScreen;

        PopulateChoices();
        BuildUi();
        WireEvents();

        UpdatePreview();
    }

    private void PopulateChoices()
    {
        foreach (var family in FontFamily.Families.OrderBy(f => f.Name))
            _cmbFont.Items.Add(family.Name);
        _cmbFont.SelectedItem = _cmbFont.Items.Contains("Arial") ? "Arial"
            : _cmbFont.Items.Count > 0 ? _cmbFont.Items[0] : null;

        _cmbRotate.Items.AddRange(["0", "90", "180", "270"]);
        _cmbRotate.SelectedItem = "270";

        _cmbDensity.Items.AddRange(["Default", "Light", "Medium", "Thick"]);
        _cmbDensity.SelectedIndex = 0;
    }

    private void BuildUi()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 300,
        };
        _split = split;

        // ---- left: settings ----
        var settings = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8),
        };
        settings.Controls.Add(Labeled("Text", _txtText, 70));
        settings.Controls.Add(Labeled("Font", _cmbFont));
        settings.Controls.Add(Bare(_chkBold, 24));
        settings.Controls.Add(Labeled("Font size (px, 0 = auto)", _numSize));
        settings.Controls.Add(Labeled("Minimum font (px)", _numMinFont));
        settings.Controls.Add(Labeled("Rotate", _cmbRotate));
        settings.Controls.Add(Bare(_chkFlip, 24));
        settings.Controls.Add(Labeled("Body length (mm)", _numLen));
        settings.Controls.Add(Labeled("Eject / tear feed (mm)", _numEject));
        settings.Controls.Add(BorderRow());
        settings.Controls.Add(Labeled("Density", _cmbDensity));
        settings.Controls.Add(ImageRow());
        settings.Controls.Add(Labeled("Printer MAC", _txtAddr));

        // ---- right: preview + actions ----
        var right = new Panel { Dock = DockStyle.Fill };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 96, Padding = new Padding(8) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(_btnPrint);
        buttons.Controls.Add(_btnCancel);
        buttons.Controls.Add(_btnInfo);
        bottom.Controls.Add(_lblStatus);
        bottom.Controls.Add(_prog);
        bottom.Controls.Add(buttons);

        right.Controls.Add(_picPreview);
        right.Controls.Add(bottom);

        split.Panel1.Controls.Add(settings);
        split.Panel2.Controls.Add(right);
        Controls.Add(split);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // SplitterDistance must be set once the control has a real size.
        _split.SplitterDistance = 330;
    }

    private void WireEvents()
    {
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); UpdatePreview(); };

        void Schedule(object? s, EventArgs e) { _previewTimer.Stop(); _previewTimer.Start(); }

        _txtText.TextChanged += Schedule;
        _cmbFont.SelectedIndexChanged += Schedule;
        _chkBold.CheckedChanged += Schedule;
        _numSize.ValueChanged += Schedule;
        _numMinFont.ValueChanged += Schedule;
        _cmbRotate.SelectedIndexChanged += Schedule;
        _chkFlip.CheckedChanged += Schedule;
        _numLen.ValueChanged += Schedule;
        _chkBorder.CheckedChanged += Schedule;
        _numBorderW.ValueChanged += Schedule;

        _btnBrowse.Click += OnBrowseImage;
        _btnClearImg.Click += (_, _) => { _imagePath = null; _txtImage.Text = ""; UpdatePreview(); };
        _btnPrint.Click += OnPrint;
        _btnCancel.Click += (_, _) => _cts?.Cancel();
        _btnInfo.Click += OnQueryInfo;

        FormClosed += (_, _) =>
        {
            _previewTimer.Dispose();
            _picPreview.Image?.Dispose();
            _cts?.Dispose();
        };
    }

    // ---- options from controls ----

    private RenderOptions ReadRenderOptions() => new()
    {
        Text = _txtText.Text,
        FontName = _cmbFont.SelectedItem?.ToString() ?? "Arial",
        FontSize = (double)_numSize.Value,
        MinFontPx = (double)_numMinFont.Value,
        Bold = _chkBold.Checked,
        Rotate = int.Parse(_cmbRotate.SelectedItem?.ToString() ?? "270"),
        Flip = _chkFlip.Checked,
        LabelLengthMm = (double)_numLen.Value,
        Border = _chkBorder.Checked,
        BorderWidth = (int)_numBorderW.Value,
        ImagePath = _imagePath,
    };

    private PrintOptions ReadPrintOptions() => new()
    {
        BtAddr = _txtAddr.Text.Trim(),
        EjectMm = (double)_numEject.Value,
        Density = _cmbDensity.SelectedIndex - 1, // Default -> -1
    };

    // ---- preview ----

    private void UpdatePreview()
    {
        try
        {
            var options = ReadRenderOptions();
            using var result = LabelRenderer.Render(options);

            var old = _picPreview.Image;
            _picPreview.Image = (Image)result.Preview.Clone();
            old?.Dispose();

            _lblStatus.Text = _imagePath is not null
                ? $"Image: {result.Rows} rows (~{result.LengthMm(options.Dpi):0.#} mm)."
                : $"{result.Lines} line(s), {result.FontPx:0.#} px, {result.Rows} rows (~{result.LengthMm(options.Dpi):0.#} mm).";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Preview error: " + ex.Message;
        }
    }

    // ---- actions ----

    private async void OnPrint(object? sender, EventArgs e)
    {
        IReadOnlyList<byte[]> bands;
        PrintOptions printOptions;
        try
        {
            var renderOptions = ReadRenderOptions();
            printOptions = ReadPrintOptions();
            using var result = LabelRenderer.Render(renderOptions);
            bands = RasterPacker.ToBands(result.Oriented, printOptions.BandRows);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Render error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _cts = new CancellationTokenSource();
        SetBusy(true);
        _prog.Value = 0;

        var progress = new Progress<PrintProgress>(p =>
        {
            _prog.Maximum = Math.Max(1, p.Total);
            _prog.Value = Math.Min(p.Current, _prog.Maximum);
            _lblStatus.Text = $"{p.Phase} {p.Current}/{p.Total}";
        });

        try
        {
            await _printer.PrintAsync(bands, printOptions, progress, _cts.Token);
            _lblStatus.Text = "Print complete.";
        }
        catch (OperationCanceledException)
        {
            _lblStatus.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Print error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _lblStatus.Text = "Error: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async void OnQueryInfo(object? sender, EventArgs e)
    {
        SetBusy(true);
        _lblStatus.Text = "Querying printer…";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var info = await _printer.QueryInfoAsync(ReadPrintOptions(), cts.Token);
            _lblStatus.Text = $"{info.Model} fw {info.Firmware}  batt {info.BatteryPercent}%  [{info.Status}]";
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Info failed: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnBrowseImage(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Images|*.png;*.bmp;*.jpg;*.jpeg;*.gif|All files|*.*",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _imagePath = dlg.FileName;
            _txtImage.Text = Path.GetFileName(dlg.FileName);
            UpdatePreview();
        }
    }

    private void SetBusy(bool busy)
    {
        _btnPrint.Enabled = !busy;
        _btnInfo.Enabled = !busy;
        _btnCancel.Enabled = busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    // ---- tiny layout helpers ----

    private static Control Labeled(string caption, Control control, int controlHeight = 24)
    {
        var panel = new Panel { Width = 292, Height = controlHeight + 20, Margin = new Padding(0, 0, 0, 6) };
        control.Dock = DockStyle.Fill;
        var label = new Label { Text = caption, Dock = DockStyle.Top, Height = 18, AutoSize = false };
        panel.Controls.Add(control);
        panel.Controls.Add(label);
        return panel;
    }

    private static Control Bare(Control control, int height)
    {
        control.Width = 292;
        control.Height = height;
        control.Margin = new Padding(0, 0, 0, 6);
        return control;
    }

    private Control BorderRow()
    {
        var panel = new Panel { Width = 292, Height = 28, Margin = new Padding(0, 0, 0, 6) };
        _chkBorder.Location = new Point(0, 3);
        _chkBorder.Width = 90;
        var lbl = new Label { Text = "width", Location = new Point(100, 6), AutoSize = true };
        _numBorderW.Location = new Point(150, 2);
        _numBorderW.Width = 60;
        panel.Controls.Add(_chkBorder);
        panel.Controls.Add(lbl);
        panel.Controls.Add(_numBorderW);
        return panel;
    }

    private Control ImageRow()
    {
        var panel = new Panel { Width = 292, Height = 50, Margin = new Padding(0, 0, 0, 6) };
        var label = new Label { Text = "Image (optional)", Dock = DockStyle.Top, Height = 18, AutoSize = false };
        _txtImage.SetBounds(0, 20, 148, 24);
        _btnBrowse.SetBounds(152, 19, 80, 26);
        _btnClearImg.SetBounds(234, 19, 56, 26);
        panel.Controls.Add(_txtImage);
        panel.Controls.Add(_btnBrowse);
        panel.Controls.Add(_btnClearImg);
        panel.Controls.Add(label);
        return panel;
    }
}

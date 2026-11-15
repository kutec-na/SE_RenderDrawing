using System.Globalization;

namespace SE_RenderDrawing;

/// <summary>
/// Abfrage von Revision, Beschreibung, Datum und Bearbeiter.
/// Die Änderungstabelle ist umschaltbar (0 = automatisch, 1…n = Tabellen in der Reihenfolge der Zeichnung),
/// jedem Feld kann eine Tabellenspalte zugewiesen werden.
/// </summary>
public sealed class RevisionDialog : Form
{
    private static string NoColumn => Lang.L("Dialog.NoColumn");

    private readonly string _draftPath;
    private readonly IReadOnlyList<DraftTable> _tables;
    private readonly DraftTable? _auto;
    private readonly MacroConfig _cfg;
    private readonly string? _presetEditor;
    private int _position; // 0 = automatisch, sonst 1-basiert in _tables
    private bool _loading;

    private readonly Button _btnPrev = new() { Text = "◀", Width = 36, Height = 28 };
    private readonly Button _btnNext = new() { Text = "▶", Width = 36, Height = 28 };
    private readonly Label _lblPosition = new() { Width = 200, Height = 28, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
    private readonly Label _lblCaption = new() { AutoSize = false, AutoEllipsis = true, UseMnemonic = false, Margin = new Padding(0, 8, 0, 0) };
    private readonly ListView _list = new()
    {
        View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
        Width = 600, Height = 170, MultiSelect = false, GridLines = true,
    };

    private readonly TextBox _txtRevision = new() { Width = 90 };
    private readonly TextBox _txtText = new() { Width = 300 };
    private readonly DateTimePicker _dtDate = new() { Width = 120, Format = DateTimePickerFormat.Custom };
    private readonly TextBox _txtEditor = new() { Width = 90 };
    private readonly Dictionary<Field, ComboBox> _combos = new();

    private readonly TextBox _txtFolder = new() { Width = 260 };
    private readonly Button _btnBrowse = new() { Text = "…", Width = 32, Height = 25 };

    private readonly CheckBox _chkAppendRev = new() { Text = Lang.L("Dialog.AppendRevision"), AutoSize = true, Margin = new Padding(0, 6, 12, 0) };
    private readonly TextBox _txtSeparator = new() { Width = 36, TextAlign = HorizontalAlignment.Center };
    private readonly NumericUpDown _numCount = new() { Width = 50, Minimum = 0, Maximum = 20 };
    private readonly ComboBox _cmbPosition = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly TextBox _txtAffix = new() { Width = 36, TextAlign = HorizontalAlignment.Center };
    private readonly Label _lblAffix = new() { AutoSize = true, Margin = new Padding(10, 6, 4, 0) };
    private readonly List<Control> _ruleControls = new();
    private readonly ToolTip _tip = new();
    private int _contentWidth = 600;

    private readonly CheckBox _chkUpdate = new() { Text = Lang.L("Dialog.UpdateViews"), AutoSize = true };
    private readonly CheckBox _chkTracker = new() { Text = Lang.L("Dialog.ClearTracker"), AutoSize = true };
    private readonly CheckBox _chkBlack = new() { Text = Lang.L("Dialog.SaveAllBlack"), AutoSize = true };
    private readonly Label _lblPdf = new() { AutoSize = false };
    private readonly Label _lblWarn = new() { AutoSize = true, ForeColor = Color.DarkRed, MaximumSize = new Size(600, 0) };

    // ---------- Ergebnis ----------
    public DraftTable? SelectedTable => _position == 0 ? _auto : _tables[_position - 1];
    public int SelectedPosition => _position;
    public string Revision => _txtRevision.Text.Trim();
    public string Description => _txtText.Text.Trim();
    public string Date => _dtDate.Value.ToString(_cfg.DateFormat, CultureInfo.InvariantCulture);
    public string Editor => _txtEditor.Text.Trim();
    public bool UpdateViews => _chkUpdate.Checked;
    public bool ClearTracker => _chkTracker.Checked;
    public bool SaveAllColorsBlack => _chkBlack.Checked;
    /// <summary>Speicherort wie eingegeben (absolut oder relativ).</summary>
    public string PdfFolder => _txtFolder.Text.Trim();
    /// <summary>Wie die Revision in den Dateinamen eingefügt wird.</summary>
    public FileNameRule FileNameRule => new()
    {
        AppendRevision = _chkAppendRev.Checked,
        Separator = _txtSeparator.Text,
        Count = (int)_numCount.Value,
        Position = _cmbPosition.SelectedIndex == 1 ? RevisionPosition.After : RevisionPosition.Before,
        Affix = _txtAffix.Text,
    };
    /// <summary>Vollständiger Pfad der PDF.</summary>
    public string PdfPath => PdfNaming.FullPath(_draftPath, Revision, PdfFolder, FileNameRule);

    /// <summary>Zugeordnete Spalte je Feld (0-basiert, −1 = keine).</summary>
    public Dictionary<Field, int> ColumnMap =>
        _combos.ToDictionary(kv => kv.Key, kv => kv.Value.SelectedIndex - 1);

    public RevisionDialog(string draftPath, IReadOnlyList<DraftTable> tables, DraftTable? auto, MacroConfig cfg,
        int startPosition, string? presetEditor, bool updateViews, bool clearTracker, string pdfFolder,
        FileNameRule fileNameRule, bool saveAllColorsBlack, string? presetDescription = null)
    {
        _draftPath = draftPath;
        _tables = tables;
        _auto = auto;
        _cfg = cfg;
        _presetEditor = string.IsNullOrWhiteSpace(presetEditor) ? null : presetEditor;
        _position = Math.Clamp(startPosition, 0, tables.Count);

        Text = Lang.L("Dialog.Title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        Font = new Font("Segoe UI", 9f);
        ShowInTaskbar = false;

        _dtDate.CustomFormat = cfg.DateFormat;
        _dtDate.Value = DateTime.Today;
        _chkUpdate.Checked = updateViews;
        _chkTracker.Checked = clearTracker;
        _chkBlack.Checked = saveAllColorsBlack;

        // --- Navigation über der Vorschau ---
        var nav = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, AutoSize = true, Anchor = AnchorStyles.None, Margin = new Padding(0, 8, 0, 2) };
        nav.Controls.Add(_btnPrev, 0, 0);
        nav.Controls.Add(_lblPosition, 1, 0);
        nav.Controls.Add(_btnNext, 2, 0);
        var navHost = new Panel { Width = _list.Width, Height = 34, Margin = new Padding(0) };
        navHost.Controls.Add(nav);
        navHost.Layout += (_, _) => nav.Location = new Point((navHost.Width - nav.Width) / 2, 0);
        _btnPrev.Click += (_, _) => Step(-1);
        _btnNext.Click += (_, _) => Step(+1);
        _tip.SetToolTip(_btnPrev, Lang.L("Dialog.PrevTable"));
        _tip.SetToolTip(_btnNext, Lang.L("Dialog.NextTable"));

        // --- Felder mit Spaltenzuordnung (Spalte 3 füllt auf → Zuordnung bündig rechts) ---
        var fields = new TableLayoutPanel { ColumnCount = 5, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        int fieldRow = 0;
        void Row(string label, Control input, Field field)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
            _combos[field] = combo;
            fields.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) }, 0, fieldRow);
            fields.Controls.Add(input, 1, fieldRow);
            fields.Controls.Add(new Label { Text = Lang.L("Dialog.ColumnArrow"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 6, 4, 0), ForeColor = SystemColors.GrayText }, 3, fieldRow);
            fields.Controls.Add(combo, 4, fieldRow);
            fieldRow++;
        }
        Row(Lang.L("Field.Revision"), _txtRevision, Field.Revision);
        Row(Lang.L("Field.Description"), _txtText, Field.Description);
        Row(Lang.L("Field.Date"), _dtDate, Field.Date);
        Row(Lang.L("Field.Editor"), _txtEditor, Field.Editor);
        _combos[Field.Revision].SelectedIndexChanged += (_, _) => { if (!_loading) { PrefillRevision(); RefreshInfo(); } };
        _combos[Field.Editor].SelectedIndexChanged += (_, _) => { if (!_loading) PrefillEditor(); };
        foreach (var c in _combos.Values) c.SelectedIndexChanged += (_, _) => { if (!_loading) RefreshInfo(); };

        // --- Speicherort: 5. Zeile der Feldtabelle, Textfeld bündig unter den Eingabefeldern, Button am rechten Rand ---
        var folderCell = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
        folderCell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderCell.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _txtFolder.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        folderCell.Controls.Add(_txtFolder, 0, 0);
        _btnBrowse.Width = _txtAffix.Width; // sitzt genau über dem Prä-/Suffix-Feld
        _btnBrowse.Height = _txtFolder.PreferredHeight + 2;
        folderCell.Controls.Add(_btnBrowse, 1, 0);
        fields.Controls.Add(new Label { Text = Lang.L("Dialog.SaveIn"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) }, 0, fieldRow);
        fields.Controls.Add(folderCell, 1, fieldRow);
        fields.SetColumnSpan(folderCell, 4);
        fieldRow++;
        _tip.SetToolTip(_txtFolder, Lang.T("Dialog.FolderTip"));
        _tip.SetToolTip(_btnBrowse, Lang.L("Dialog.BrowseTip"));
        _txtFolder.Text = pdfFolder;
        _txtFolder.TextChanged += (_, _) => { if (!_loading) RefreshInfo(); };
        _btnBrowse.Click += (_, _) => BrowseFolder();

        // --- Revision im Dateinamen (Prä-/Suffix bündig rechts) ---
        var ruleRow = new TableLayoutPanel { ColumnCount = 10, RowCount = 1, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        for (int i = 0; i < 10; i++)
            ruleRow.ColumnStyles.Add(i == 7 ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.AutoSize));
        Label RuleLabel(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(10, 6, 4, 0) };
        _cmbPosition.Items.AddRange([Lang.L("Dialog.PositionBefore"), Lang.L("Dialog.PositionAfter")]);
        _chkAppendRev.Anchor = AnchorStyles.Left;
        ruleRow.Controls.Add(_chkAppendRev, 0, 0);
        _lblAffix.Anchor = AnchorStyles.Left;
        var ruleCells = new (Control Label, Control Input, int Col)[]
        {
            (RuleLabel(Lang.L("Dialog.Separator")), _txtSeparator, 1),
            (RuleLabel(Lang.L("Dialog.Count")), _numCount, 3),
            (RuleLabel(Lang.L("Dialog.Position")), _cmbPosition, 5),
            (_lblAffix, _txtAffix, 8),
        };
        foreach (var (label, input, col) in ruleCells)
        {
            ruleRow.Controls.Add(label, col, 0);
            ruleRow.Controls.Add(input, col + 1, 0);
            _ruleControls.Add(label);
            _ruleControls.Add(input);
        }
        _tip.SetToolTip(_txtSeparator, Lang.T("Dialog.SeparatorTip"));
        _tip.SetToolTip(_numCount, Lang.T("Dialog.CountTip"));
        _tip.SetToolTip(_cmbPosition, Lang.T("Dialog.PositionTip"));
        _tip.SetToolTip(_txtAffix, Lang.T("Dialog.AffixTip"));

        _chkAppendRev.Checked = fileNameRule.AppendRevision;
        _txtSeparator.Text = fileNameRule.Separator;
        _numCount.Value = Math.Clamp(fileNameRule.Count, (int)_numCount.Minimum, (int)_numCount.Maximum);
        _cmbPosition.SelectedIndex = fileNameRule.Position == RevisionPosition.After ? 1 : 0;
        _txtAffix.Text = fileNameRule.Affix;
        UpdateRuleControls();

        _chkAppendRev.CheckedChanged += (_, _) => { UpdateRuleControls(); RefreshInfo(); };
        _txtSeparator.TextChanged += (_, _) => RefreshInfo();
        _numCount.ValueChanged += (_, _) => RefreshInfo();
        _cmbPosition.SelectedIndexChanged += (_, _) => { UpdateRuleControls(); RefreshInfo(); };
        _txtAffix.TextChanged += (_, _) => RefreshInfo();

        // --- Buttons ---
        var btnOk = new Button { Text = Lang.L("Dialog.Ok"), AutoSize = true };
        var btnCancel = new Button { Text = Lang.L("Dialog.Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        btnOk.Click += (_, _) => OnOk();
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = false, Height = 36, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.Add(btnCancel);
        buttons.Controls.Add(btnOk);

        // --- Gesamtbreite: breiteste Zeile, alle Zeilen auf diese Breite ziehen ---
        _contentWidth = new[]
        {
            600,
            fields.GetPreferredSize(Size.Empty).Width,
            ruleRow.GetPreferredSize(Size.Empty).Width,
        }.Max();
        foreach (var row in new Control[] { fields, ruleRow })
        {
            row.MinimumSize = new Size(_contentWidth, 0);
            row.MaximumSize = new Size(_contentWidth, 0);
        }
        // Vorschau links bündig mit den Beschriftungen, rechts bündig mit den Auswahlfeldern (Rand 3 px)
        _list.Margin = new Padding(0, 3, 0, 3);
        _list.Width = _contentWidth - 3;
        navHost.Width = _contentWidth;
        buttons.Width = _contentWidth;
        // Kopfzeile der Tabelle: genau eine Zeile, zu lang → am Ende mit … gekürzt
        _lblCaption.Size = new Size(_contentWidth - 3, TextRenderer.MeasureText("Ag", _lblCaption.Font).Height + 4);
        // Warnzeile: genau eine Zeile, mehrere Hinweise mit " · " getrennt, zu lange am Ende mit … gekürzt (Tooltip zeigt alle)
        _lblWarn.AutoSize = false;
        _lblWarn.AutoEllipsis = true;
        _lblWarn.UseMnemonic = false;
        _lblWarn.MaximumSize = Size.Empty;
        _lblWarn.Size = new Size(_contentWidth - 3, TextRenderer.MeasureText("Ag", _lblWarn.Font).Height + 4);
        _lblWarn.Margin = new Padding(0, 4, 0, 0);
        // Pfadzeile: genau eine Zeile, lange Pfade vorne mit "..." gekürzt
        _lblPdf.AutoSize = false;
        _lblPdf.AutoEllipsis = false;
        _lblPdf.UseMnemonic = false; // "&" in Pfaden anzeigen
        _lblPdf.MaximumSize = Size.Empty;
        _lblPdf.Size = new Size(_contentWidth - 3, TextRenderer.MeasureText("Ag", _lblPdf.Font).Height + 4);
        _lblPdf.Margin = new Padding(0, 4, 0, 4);

        var main = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        main.Controls.Add(new Label { Text = Lang.L("Dialog.DrawingLabel") + " " + Path.GetFileName(draftPath), UseMnemonic = false, AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        main.Controls.Add(_lblCaption);
        main.Controls.Add(navHost);
        main.Controls.Add(_list);
        main.Controls.Add(fields);       // Revision, Beschreibung, Datum, Bearbeiter, Speichern in
        main.Controls.Add(ruleRow);      // Revision anhängen …
        main.Controls.Add(_chkUpdate);
        main.Controls.Add(_chkTracker);
        main.Controls.Add(_chkBlack);
        main.Controls.Add(_lblPdf);      // aufgelöster Pfad und Dateiname
        main.Controls.Add(_lblWarn);     // Status und Fehlermeldungen
        main.Controls.Add(buttons);
        Controls.Add(main);

        _txtRevision.TextChanged += (_, _) => { if (!_loading) RefreshInfo(); };
        Shown += (_, _) => (_txtRevision.Text.Length > 0 ? _txtText : _txtRevision).Focus();

        _txtEditor.Text = _presetEditor ?? string.Empty;
        _txtText.Text = presetDescription ?? string.Empty;
        ShowTable();
    }

    /// <summary>
    /// Zeigt Präfix + Text in genau einer Zeile. Passt der Text nicht, werden die ersten Zeichen
    /// abgeschnitten und durch "..." ersetzt; der vollständige Text steht im Tooltip.
    /// </summary>
    private void SetPdfLine(string prefix, string text)
    {
        const string dots = "...";
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        int avail = _lblPdf.Width - _lblPdf.Padding.Horizontal - 6;
        bool Fits(string s) => TextRenderer.MeasureText(s, _lblPdf.Font, new Size(int.MaxValue, int.MaxValue), flags).Width <= avail;

        string shown = prefix + text;
        if (!Fits(shown))
        {
            // kleinste Anzahl abgeschnittener Zeichen suchen, bei der es passt (binäre Suche)
            int lo = 1, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (Fits(prefix + dots + text[mid..])) hi = mid; else lo = mid + 1;
            }
            shown = prefix + dots + text[lo..];
        }
        _lblPdf.Text = shown;
        _tip.SetToolTip(_lblPdf, prefix + text);
    }

    private void UpdateRuleControls()
    {
        foreach (var c in _ruleControls) c.Enabled = _chkAppendRev.Checked;
        _lblAffix.Text = _cmbPosition.SelectedIndex == 1 ? Lang.L("Dialog.Suffix") : Lang.L("Dialog.Prefix");
    }

    private void Step(int delta)
    {
        int count = _tables.Count + 1; // inkl. "automatisch"
        _position = ((_position + delta) % count + count) % count;
        ShowTable();
    }

    private void ShowTable()
    {
        _loading = true;
        try
        {
            var t = SelectedTable;
            _btnPrev.Enabled = _btnNext.Enabled = _tables.Count > 0;
            _lblPosition.Text = _position == 0 ? Lang.L("Dialog.Automatic") : $"{_position}";

            // Kopfzeile: "Änderungstabelle im Schriftfeld: Tabelle x von n („Name“)"
            var info = _tables.Count == 0 ? Lang.T("Dialog.NoTables")
                     : t is null ? Lang.T("Dialog.NoMatchingTable")
                     : Lang.T("Dialog.TableRef", t.Number, _tables.Count, t.Name);
            _lblCaption.Text = Lang.L("Dialog.TableCaption") + " " + info;
            _tip.SetToolTip(_lblCaption, _lblCaption.Text);

            // Vorschau in der Reihenfolge wie im Schriftfeld
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Columns.Clear();
            if (t is not null)
            {
                int width = (_list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4) / Math.Max(1, t.ColumnCount);
                for (int c = 0; c < t.ColumnCount; c++)
                    _list.Columns.Add(ColumnTitle(t, c), width);
                var rows = t.ReverseDisplay ? Enumerable.Reverse(t.Rows) : t.Rows;
                foreach (var r in rows) _list.Items.Add(new ListViewItem(r));
            }
            _list.EndUpdate();
            if (t is not null && t.Capacity > 0)
            {
                // neuesten Eintrag (bzw. bei leerer Tabelle die erste Zeile) sichtbar machen
                int newest = Math.Max(0, t.LastFilledRow);
                _list.EnsureVisible(t.ReverseDisplay ? t.Capacity - 1 - newest : newest);
            }

            // Spaltenzuordnung
            var map = t?.GuessColumns(_cfg);
            foreach (var (field, combo) in _combos)
            {
                combo.Items.Clear();
                combo.Items.Add(NoColumn);
                if (t is not null)
                    for (int c = 0; c < t.ColumnCount; c++) combo.Items.Add($"{c + 1}: {ColumnTitle(t, c)}");
                combo.SelectedIndex = map is null ? 0 : map[field] + 1;
                combo.Enabled = t is not null;
            }
        }
        finally { _loading = false; }

        PrefillRevision();
        PrefillEditor();
        RefreshInfo();
    }

    private static string ColumnTitle(DraftTable t, int c) =>
        t.Headers[c].Length > 0 ? t.Headers[c] : $"{Lang.L("Dialog.ColumnN")} {c + 1}";

    private int Column(Field f) => _combos[f].SelectedIndex - 1;

    /// <summary>Revision = letzter Eintrag der Revisionsspalte + 1; sonst leer lassen.</summary>
    private void PrefillRevision()
    {
        var t = SelectedTable;
        int col = Column(Field.Revision);
        string text = string.Empty;
        if (t is not null && col >= 0)
        {
            var last = t.LastValue(col);
            text = last is null ? _cfg.FirstRevision : RevisionCode.Next(last) ?? string.Empty;
        }
        _loading = true;
        _txtRevision.Text = text;
        _loading = false;
    }

    /// <summary>Bearbeiter = zuletzt verwendet (Konfig), sonst letzter Eintrag der Bearbeiterspalte; sonst leer.</summary>
    private void PrefillEditor()
    {
        if (_presetEditor is not null) return;
        var t = SelectedTable;
        int col = Column(Field.Editor);
        _txtEditor.Text = t is not null && col >= 0 ? t.LastValue(col) ?? string.Empty : string.Empty;
    }

    private void RefreshInfo()
    {
        var rev = Revision.Length > 0 ? Revision : "XX";
        var warn = new List<string>();
        string? pdf = null;
        try
        {
            pdf = PdfNaming.FullPath(_draftPath, rev, PdfFolder, FileNameRule);
            SetPdfLine(Lang.L("Dialog.PdfLabel") + " ", pdf);
            _lblPdf.ForeColor = SystemColors.ControlText;
            if (!Directory.Exists(Path.GetDirectoryName(pdf)))
                warn.Add(Lang.T("Warn.FolderMissing"));
        }
        catch (Exception ex)
        {
            SetPdfLine(Lang.L("Dialog.InvalidFolderLabel") + " ", ex.Message);
            _lblPdf.ForeColor = Color.DarkRed;
        }

        var t = SelectedTable;
        if (t is not null)
        {
            int revCol = Column(Field.Revision);
            if (t.IsFull)
            {
                var oldest = revCol >= 0 ? t.Rows[0][revCol] : string.Join(" | ", t.Rows[0]);
                warn.Add(Lang.T("Warn.TableFull", oldest));
            }
            if (revCol >= 0 && Revision.Length > 0 && t.ContainsValue(revCol, Revision))
                warn.Add(Lang.T("Warn.RevisionExists", Revision));
            if (ColumnMap.Values.All(c => c < 0))
                warn.Add(Lang.T("Warn.NoColumn"));
            var dup = ColumnMap.Where(kv => kv.Value >= 0).GroupBy(kv => kv.Value).FirstOrDefault(g => g.Count() > 1);
            if (dup is not null)
                warn.Add(Lang.T("Warn.DuplicateColumn", dup.Key + 1));
        }
        if (pdf is not null && File.Exists(pdf))
            warn.Add(Lang.T("Warn.PdfExists"));
        _lblWarn.Text = string.Join("  ·  ", warn);
        _tip.SetToolTip(_lblWarn, string.Join(Environment.NewLine, warn));
    }

    private void OnOk()
    {
        var t = SelectedTable;
        var map = ColumnMap;

        var rule = FileNameRule;
        bool revInName = rule.AppendRevision;
        bool revInTable = t is not null && map[Field.Revision] >= 0;
        if (Revision.Length == 0 && (revInName || revInTable))
        {
            Complain(Lang.T(revInName ? "Msg.RevisionRequiredPdf" : "Msg.RevisionRequired"), _txtRevision);
            return;
        }
        var invalid = Path.GetInvalidFileNameChars();
        if (revInName)
        {
            if (Revision.IndexOfAny(invalid) >= 0) { Complain(Lang.T("Msg.RevisionInvalidChars"), _txtRevision); return; }
            if (rule.Separator.Length == 0 && rule.Count > 0) { Complain(Lang.T("Msg.SeparatorRequired"), _txtSeparator); return; }
            if (rule.Separator.IndexOfAny(invalid) >= 0) { Complain(Lang.T("Msg.SeparatorInvalid"), _txtSeparator); return; }
            if (rule.Affix.IndexOfAny(invalid) >= 0) { Complain(Lang.T("Msg.AffixInvalid"), _txtAffix); return; }
        }

        if (t is not null)
        {
            if (map.Where(kv => kv.Value >= 0).GroupBy(kv => kv.Value).Any(g => g.Count() > 1))
            { Complain(Lang.T("Msg.DuplicateColumn"), _combos[Field.Revision]); return; }
            if (map[Field.Description] >= 0 && Description.Length == 0) { Complain(Lang.T("Msg.DescriptionRequired"), _txtText); return; }
            if (map[Field.Editor] >= 0 && Editor.Length == 0) { Complain(Lang.T("Msg.EditorRequired"), _txtEditor); return; }

            if (map.Values.All(c => c < 0) && !Confirm(Lang.T("Msg.NoColumnConfirm")))
                return;
            if (map[Field.Revision] >= 0 && t.ContainsValue(map[Field.Revision], Revision) &&
                !Confirm(Lang.T("Msg.RevisionExistsConfirm", Revision)))
                return;
        }
        else if (!Confirm(Lang.T("Msg.NoTableConfirm")))
            return;

        string pdf;
        try { pdf = PdfPath; }
        catch (Exception ex) { Complain(Lang.T("Msg.InvalidFolder", ex.Message), _txtFolder); return; }

        var folder = Path.GetDirectoryName(pdf)!;
        if (!Directory.Exists(folder))
        {
            if (!Confirm(Lang.T("Msg.FolderCreateConfirm", folder)))
                return;
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex) { Complain(Lang.T("Msg.FolderCreateFailed", ex.Message), _txtFolder); return; }
        }
        if (File.Exists(pdf) && !Confirm(Lang.T("Msg.OverwriteConfirm", pdf)))
            return;

        DialogResult = DialogResult.OK;
        Close();
    }

    private void BrowseFolder()
    {
        string start;
        try { start = PdfNaming.ResolveFolder(_draftPath, PdfFolder); }
        catch { start = Path.GetDirectoryName(_draftPath)!; }
        while (!Directory.Exists(start) && Path.GetDirectoryName(start) is { } parent) start = parent;

        using var fbd = new FolderBrowserDialog
        {
            Description = Lang.L("Dialog.FolderBrowserTitle"),
            UseDescriptionForTitle = true,
            InitialDirectory = start,
            ShowNewFolderButton = true,
        };
        if (fbd.ShowDialog(this) == DialogResult.OK)
            _txtFolder.Text = PdfNaming.ToDisplayFolder(_draftPath, fbd.SelectedPath);
    }

    private bool Confirm(string msg) =>
        MessageBox.Show(this, msg, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

    private void Complain(string msg, Control focus)
    {
        MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        focus.Focus();
    }
}

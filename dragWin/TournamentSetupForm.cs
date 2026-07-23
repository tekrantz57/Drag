namespace DragWin;

public sealed class TournamentSetupForm : Form
{
    private readonly RaceRepository repository;
    private readonly TournamentPlanner planner = new();
    private readonly CheckedListBox carList = new()
    {
        Dock = DockStyle.Fill,
        CheckOnClick = true,
        DisplayMember = nameof(Car.DisplayName),
        IntegralHeight = false
    };
    private readonly TextBox tournamentNameInput = new()
    {
        Text = $"Tournament {DateTime.Now:yyyy-MM-dd}",
        Dock = DockStyle.Fill
    };
    private readonly ComboBox laneCountSelector = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 72
    };
    private readonly Label selectionSummary = new()
    {
        AutoSize = true,
        Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
        ForeColor = Color.FromArgb(35, 91, 145)
    };
    private readonly DataGridView roundPreview = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.Fixed3D,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };
    private readonly Label previewNotice = new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText
    };
    private readonly Button createButton = new()
    {
        Text = "Create Tournament",
        AutoSize = true,
        BackColor = Color.FromArgb(35, 91, 145),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Padding = new Padding(12, 4, 12, 4)
    };
    private RoundPlan? previewRound;
    private bool setupActionRunning;
    private bool suppressPreviewRefresh;

    public TournamentSetupForm(RaceRepository repository)
    {
        this.repository = repository;
        Text = "Create Tournament";
        MinimumSize = new Size(900, 600);
        Size = new Size(1050, 700);
        StartPosition = FormStartPosition.CenterParent;

        laneCountSelector.Items.AddRange(["2", "4"]);
        laneCountSelector.SelectedItem = "4";
        UiStyles.ConfigurePrimaryButton(createButton, UiStyles.BlueAction);

        roundPreview.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Heat",
            HeaderText = "Heat",
            FillWeight = 35
        });
        roundPreview.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Lane",
            HeaderText = "Lane",
            FillWeight = 35
        });
        roundPreview.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Racer",
            HeaderText = "Racer",
            FillWeight = 90
        });
        roundPreview.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Car",
            HeaderText = "Car",
            FillWeight = 90
        });
        roundPreview.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Dial",
            HeaderText = "Dial",
            FillWeight = 45
        });
        roundPreview.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Notes",
            HeaderText = "Notes",
            FillWeight = 90
        });

        var manageButton = new Button { Text = "Manage Racers && Cars", AutoSize = true };
        var selectAllButton = new Button { Text = "Select All", AutoSize = true };
        var clearButton = new Button { Text = "Clear", AutoSize = true };
        manageButton.Click += (_, _) => ManageRacersAndCars();
        selectAllButton.Click += (_, _) => SetAllCarsChecked(true);
        clearButton.Click += (_, _) => SetAllCarsChecked(false);
        createButton.Click += (_, _) => CreateTournament();
        carList.ItemCheck += (_, _) =>
        {
            if (!suppressPreviewRefresh)
            {
                BeginInvoke(RefreshPreview);
            }
        };
        laneCountSelector.SelectedIndexChanged += (_, _) => RefreshPreview();
        tournamentNameInput.TextChanged += (_, _) => UpdateCreateButton();

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 5,
            Margin = new Padding(0, 0, 0, 10)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label { Text = "Tournament name", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        header.Controls.Add(tournamentNameInput, 1, 0);
        header.Controls.Add(new Label { Text = "Lanes", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
        header.Controls.Add(laneCountSelector, 3, 0);
        header.Controls.Add(manageButton, 4, 0);

        var entrantHeader = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 4)
        };
        entrantHeader.Controls.Add(new Label
        {
            Text = "Entrants",
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 7, 12, 0)
        });
        entrantHeader.Controls.Add(selectAllButton);
        entrantHeader.Controls.Add(clearButton);

        var previewHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 4)
        };
        previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        previewHeader.Controls.Add(new Label
        {
            Text = "Round 1 Preview",
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Anchor = AnchorStyles.Left
        }, 0, 0);
        previewHeader.Controls.Add(selectionSummary, 1, 0);

        var entrantPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        entrantPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        entrantPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        entrantPanel.Controls.Add(entrantHeader, 0, 0);
        entrantPanel.Controls.Add(carList, 0, 1);

        var previewPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        previewPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewPanel.Controls.Add(previewHeader, 0, 0);
        previewPanel.Controls.Add(roundPreview, 0, 1);
        previewPanel.Controls.Add(previewNotice, 0, 2);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        split.Panel1.Controls.Add(entrantPanel);
        split.Panel2.Controls.Add(previewPanel);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var cancelButton = new Button { Text = "Close", AutoSize = true };
        cancelButton.Click += (_, _) => Close();
        AcceptButton = createButton;
        CancelButton = cancelButton;
        footer.Controls.Add(createButton);
        footer.Controls.Add(cancelButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(split, 0, 1);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);
        Shown += (_, _) => UiStyles.SetSplitterDistanceWhenSized(split, 330, 260, 440);

        RefreshCars();
    }

    private void ManageRacersAndCars()
    {
        var checkedIds = CheckedCarIds();
        using var form = new RacerCarManagerForm(repository);
        form.ShowDialog(this);
        RefreshCars(checkedIds);
    }

    private void SetAllCarsChecked(bool isChecked)
    {
        suppressPreviewRefresh = true;
        try
        {
            for (var index = 0; index < carList.Items.Count; index++)
            {
                carList.SetItemChecked(index, isChecked);
            }
        }
        finally
        {
            suppressPreviewRefresh = false;
        }
        BeginInvoke(RefreshPreview);
    }

    private void RefreshCars(IReadOnlySet<long>? checkedIds = null)
    {
        suppressPreviewRefresh = true;
        try
        {
            carList.Items.Clear();
            foreach (var car in repository.GetCars())
            {
                var index = carList.Items.Add(car);
                if (checkedIds?.Contains(car.Id) == true)
                {
                    carList.SetItemChecked(index, true);
                }
            }
        }
        finally
        {
            suppressPreviewRefresh = false;
        }
        RefreshPreview();
    }

    private HashSet<long> CheckedCarIds() => carList.CheckedItems
        .Cast<Car>()
        .Select(car => car.Id)
        .ToHashSet();

    private void RefreshPreview()
    {
        var selectedCars = carList.CheckedItems.Cast<Car>().ToArray();
        roundPreview.Rows.Clear();
        previewRound = null;

        if (selectedCars.Length == 0)
        {
            selectionSummary.Text = "No entrants selected";
            previewNotice.Text = "Select cars to preview the opening round.";
            UpdateCreateButton();
            return;
        }

        var laneCount = int.Parse((string)laneCountSelector.SelectedItem!);
        previewRound = planner.CreateRound(selectedCars, laneCount, 1);
        var duplicateWarnings = 0;
        foreach (var heat in previewRound.Heats)
        {
            var duplicateRacerIds = heat.Entries
                .GroupBy(entry => entry.Car.RacerId)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet();

            foreach (var entry in heat.Entries.OrderBy(entry => entry.LaneNumber))
            {
                var notes = entry.IsBye ? "BYE - advances" : string.Empty;
                if (duplicateRacerIds.Contains(entry.Car.RacerId))
                {
                    notes = notes.Length == 0 ? "Same racer in heat" : $"{notes}; same racer in heat";
                    duplicateWarnings++;
                }
                var rowIndex = roundPreview.Rows.Add(
                    heat.HeatNumber,
                    entry.LaneNumber,
                    entry.Car.RacerName,
                    entry.Car.Name,
                    (entry.DialMilliseconds / 1000M).ToString("0.000"),
                    notes);
                if (entry.IsBye)
                {
                    roundPreview.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(232, 243, 252);
                }
                else if (duplicateRacerIds.Contains(entry.Car.RacerId))
                {
                    roundPreview.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(255, 244, 214);
                }
            }
        }

        selectionSummary.Text =
            $"{selectedCars.Length} entrant{(selectedCars.Length == 1 ? "" : "s")}  |  " +
            $"{previewRound.Heats.Count} heat{(previewRound.Heats.Count == 1 ? "" : "s")}";
        previewNotice.Text = duplicateWarnings == 0
            ? $"Random seed {previewRound.RandomSeed}. Lane assignments are saved when the tournament is created."
            : "Amber rows indicate cars belonging to the same racer could not be separated.";
        previewNotice.ForeColor = duplicateWarnings == 0
            ? SystemColors.GrayText
            : Color.FromArgb(145, 91, 0);
        UpdateCreateButton();
    }

    private void UpdateCreateButton()
    {
        createButton.Enabled =
            !setupActionRunning &&
            previewRound is not null &&
            !string.IsNullOrWhiteSpace(tournamentNameInput.Text);
    }

    private void CreateTournament()
    {
        if (setupActionRunning || previewRound is null)
        {
            return;
        }

        setupActionRunning = true;
        UpdateCreateButton();
        try
        {
            var selectedCars = carList.CheckedItems.Cast<Car>().ToArray();
            var laneCount = int.Parse((string)laneCountSelector.SelectedItem!);
            var tournament = repository.CreateTournament(
                tournamentNameInput.Text,
                laneCount,
                selectedCars.Select(car => car.Id).ToArray());
            repository.SaveRound(tournament.Id, previewRound);
            MessageBox.Show(
                this,
                $"{tournament.Name} was created with {selectedCars.Length} entrants.",
                "Tournament created",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Tournament could not be created");
        }
        finally
        {
            setupActionRunning = false;
            UpdateCreateButton();
        }
    }
}

namespace DragWin;

public sealed class RacerCarManagerForm : Form
{
    private readonly RaceRepository repository;
    private readonly ListBox carList = new()
    {
        Dock = DockStyle.Fill,
        DisplayMember = nameof(Car.DisplayName),
        IntegralHeight = false
    };
    private readonly ComboBox racerSelector = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList,
        DisplayMember = nameof(Racer.Name)
    };
    private readonly TextBox carNameInput = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown dialInput = new()
    {
        DecimalPlaces = 3,
        Increment = 0.001M,
        Minimum = 0.100M,
        Maximum = 60.000M,
        Value = 10.000M,
        Width = 90
    };

    public RacerCarManagerForm(RaceRepository repository)
    {
        this.repository = repository;
        Text = "Manage Racers and Cars";
        MinimumSize = new Size(720, 480);
        Size = new Size(820, 560);
        StartPosition = FormStartPosition.CenterParent;

        var racerNameInput = new TextBox { Dock = DockStyle.Fill };
        var addRacerButton = new Button { Text = "Add Racer", AutoSize = true };
        var addCarButton = new Button
        {
            Text = "Add Car",
            AutoSize = true,
            BackColor = Color.FromArgb(35, 91, 145),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        var updateCarButton = new Button { Text = "Update Car", AutoSize = true };
        var retireCarButton = new Button
        {
            Text = "Retire Car",
            AutoSize = true,
            ForeColor = Color.FromArgb(158, 45, 45)
        };
        UiStyles.ConfigurePrimaryButton(addCarButton, UiStyles.BlueAction);

        addRacerButton.Click += (_, _) => RunDatabaseAction(() =>
        {
            var racer = repository.AddRacer(racerNameInput.Text);
            racerNameInput.Clear();
            RefreshData(selectedRacerId: racer.Id);
        });
        addCarButton.Click += (_, _) => AddCar();
        updateCarButton.Click += (_, _) => UpdateCar();
        retireCarButton.Click += (_, _) => RetireCar();
        carList.SelectedIndexChanged += (_, _) => LoadSelectedCar();

        var racerRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            Padding = new Padding(0, 0, 0, 10)
        };
        racerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        racerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        racerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        racerRow.Controls.Add(new Label { Text = "New racer", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        racerRow.Controls.Add(racerNameInput, 1, 0);
        racerRow.Controls.Add(addRacerButton, 2, 0);

        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(12, 0, 0, 0)
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editor.Controls.Add(new Label { Text = "Racer", AutoSize = true, Margin = new Padding(0, 7, 10, 8) }, 0, 0);
        editor.Controls.Add(racerSelector, 1, 0);
        editor.Controls.Add(new Label { Text = "Car name", AutoSize = true, Margin = new Padding(0, 7, 10, 8) }, 0, 1);
        editor.Controls.Add(carNameInput, 1, 1);
        editor.Controls.Add(new Label { Text = "Default dial", AutoSize = true, Margin = new Padding(0, 7, 10, 8) }, 0, 2);
        editor.Controls.Add(dialInput, 1, 2);

        var editorButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        editorButtons.Controls.AddRange([addCarButton, updateCarButton, retireCarButton]);
        editor.Controls.Add(editorButtons, 1, 3);
        editor.Controls.Add(new Label
        {
            Text = "Retired cars remain in tournament history but are hidden from new events.",
            AutoSize = true,
            MaximumSize = new Size(300, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 12, 3, 3)
        }, 1, 4);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill
        };
        split.Panel1.Controls.Add(carList);
        split.Panel2.Controls.Add(editor);

        var closeButton = new Button { Text = "Close", AutoSize = true };
        closeButton.Click += (_, _) => Close();
        CancelButton = closeButton;
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };
        footer.Controls.Add(closeButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(racerRow, 0, 0);
        layout.Controls.Add(split, 0, 1);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);
        Shown += (_, _) => UiStyles.SetSplitterDistanceWhenSized(split, 380, 280, 300);

        RefreshData();
    }

    private void AddCar()
    {
        if (racerSelector.SelectedItem is not Racer racer)
        {
            MessageBox.Show(this, "Add or select a racer first.", Text);
            return;
        }
        RunDatabaseAction(() =>
        {
            var car = repository.AddCar(racer.Id, carNameInput.Text, DialMilliseconds());
            carNameInput.Clear();
            RefreshData(car.Id, racer.Id);
        });
    }

    private void UpdateCar()
    {
        if (carList.SelectedItem is not Car car || racerSelector.SelectedItem is not Racer racer)
        {
            MessageBox.Show(this, "Select a car to update.", Text);
            return;
        }
        RunDatabaseAction(() =>
        {
            var updated = repository.UpdateCar(car.Id, racer.Id, carNameInput.Text, DialMilliseconds());
            RefreshData(updated.Id, racer.Id);
        });
    }

    private void RetireCar()
    {
        if (carList.SelectedItem is not Car car)
        {
            MessageBox.Show(this, "Select a car to retire.", Text);
            return;
        }
        if (MessageBox.Show(
                this,
                $"Retire {car.DisplayName}?",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        RunDatabaseAction(() =>
        {
            repository.RetireCar(car.Id);
            carNameInput.Clear();
            RefreshData();
        });
    }

    private int DialMilliseconds() => decimal.ToInt32(dialInput.Value * 1000M);

    private void LoadSelectedCar()
    {
        if (carList.SelectedItem is not Car car)
        {
            return;
        }
        racerSelector.SelectedItem = racerSelector.Items.Cast<Racer>()
            .FirstOrDefault(racer => racer.Id == car.RacerId);
        carNameInput.Text = car.Name;
        dialInput.Value = Math.Clamp(car.DefaultDialMilliseconds / 1000M, dialInput.Minimum, dialInput.Maximum);
    }

    private void RefreshData(long? selectedCarId = null, long? selectedRacerId = null)
    {
        racerSelector.DataSource = repository.GetRacers().ToList();
        if (selectedRacerId.HasValue)
        {
            racerSelector.SelectedItem = racerSelector.Items.Cast<Racer>()
                .FirstOrDefault(racer => racer.Id == selectedRacerId);
        }
        carList.Items.Clear();
        foreach (var car in repository.GetCars())
        {
            carList.Items.Add(car);
        }
        if (selectedCarId.HasValue)
        {
            carList.SelectedItem = carList.Items.Cast<Car>()
                .FirstOrDefault(car => car.Id == selectedCarId);
        }
    }

    private void RunDatabaseAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Operation failed");
        }
    }
}

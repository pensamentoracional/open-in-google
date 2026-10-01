using SheetsWindows.Infrastructure;

namespace SheetsWindows.Windows;

internal sealed class SetupForm : Form
{
    public int ExitCode { get; private set; }
    private readonly TextBox client = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox folder = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly CheckBox consent = new() { AutoSize = true, MaximumSize = new Size(580, 0), Text = "Declaro que a pasta é local e não sincronizada. Aceito converter XLSX para Sheets, substituir o original por atalho e conservar o backup privado." };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(580, 0) };
    public SetupForm()
    {
        Text = "Configurar piloto — Sheets Windows"; ClientSize = new Size(640, 410); AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 9, AutoScroll = true };
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(580, 0), Text = "Piloto XLSX até 5 MiB, sem VBA. A conversão pode perder recursos do Excel. O backup guarda o original inicial; não existe sincronização de volta. Selecione o JSON OAuth de aplicativo desktop do seu projeto Google e uma pasta dedicada." });
        var chooseClient = new Button { AutoSize = true, Text = "Escolher JSON OAuth desktop" };
        chooseClient.Click += (_, _) => { using var dialog = new OpenFileDialog { Filter = "JSON OAuth|*.json", CheckFileExists = true }; if (dialog.ShowDialog(this) == DialogResult.OK) client.Text = dialog.FileName; };
        layout.Controls.Add(chooseClient); layout.Controls.Add(client);
        var chooseFolder = new Button { AutoSize = true, Text = "Escolher pasta local não sincronizada" };
        chooseFolder.Click += (_, _) => { using var dialog = new FolderBrowserDialog(); if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath; };
        layout.Controls.Add(chooseFolder); layout.Controls.Add(folder); layout.Controls.Add(consent);
        var save = new Button { Text = "Salvar configuração", AutoSize = true };
        save.Click += async (_, _) =>
        {
            save.Enabled = false;
            try
            {
                var storage = LocalStorage.ForCurrentUser();
                var jsonPath = string.IsNullOrEmpty(client.Text) ? LauncherConfiguration.ClientPath(storage) : client.Text;
                if (new FileInfo(jsonPath).Length > 65536) throw new InvalidDataException();
                await using var held = await new FileOperationLock(storage.LocksPath).AcquireAsync("windows-registration");
                PilotSetup.Configure(storage, await File.ReadAllTextAsync(jsonPath), folder.Text, consent.Checked);
                new WindowsAssociationRegistration(Microsoft.Win32.Registry.CurrentUser).Register(Environment.ProcessPath!);
                WindowsAssociationRegistration.NotifyShell(); ExitCode = 0;
                status.Text = "Configuração salva. Feche esta tela, autorize o Google e escolha o aplicativo padrão na tela principal. Se uma planilha estava aguardando, abra-a novamente.";
            }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { ExitCode = 1; status.Text = "Confira o JSON desktop, a pasta dedicada e a declaração. Configurações existentes são preservadas; mudanças de cliente ou pasta exigem migração."; }
            finally { save.Enabled = true; }
        };
        layout.Controls.Add(save); layout.Controls.Add(status);
        layout.Controls.Add(new Button { Text = "Fechar", AutoSize = true, DialogResult = DialogResult.Cancel }); Controls.Add(layout);
        var policy = PilotSetup.PolicyPath(LocalStorage.ForCurrentUser()); if (File.Exists(policy)) folder.Text = File.ReadAllText(policy);
    }
}

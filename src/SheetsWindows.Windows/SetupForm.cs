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
        Text = "Configurar piloto — ZagoSheetsWin"; ClientSize = new Size(760, 760); AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 18, AutoScroll = true };
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(580, 0), Text = "Piloto: arquivos até 20 MiB, sem VBA. CSV/TSV: até 500 mil células, 50 mil linhas e mil colunas. A conversão pode perder recursos do Excel. O backup guarda o original inicial; não existe sincronização de volta. Selecione o JSON OAuth de aplicativo desktop do seu projeto Google e uma pasta dedicada." });
        var chooseClient = new Button { AutoSize = true, Text = "Escolher JSON OAuth desktop" };
        chooseClient.Click += (_, _) => { using var dialog = new OpenFileDialog { Filter = "JSON OAuth|*.json", CheckFileExists = true }; if (dialog.ShowDialog(this) == DialogResult.OK) client.Text = dialog.FileName; };
        layout.Controls.Add(chooseClient); layout.Controls.Add(client);
        var chooseFolder = new Button { AutoSize = true, Text = "Escolher pasta local não sincronizada" };
        chooseFolder.Click += (_, _) => { using var dialog = new FolderBrowserDialog(); if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath; };
        layout.Controls.Add(chooseFolder); layout.Controls.Add(folder); layout.Controls.Add(consent);
        var extended = new CheckBox { AutoSize = true, MaximumSize = new Size(580, 0), Text = "Habilitar CSV, TSV, ODS e XLS. CSV/TSV serão tratados como texto literal; ODS simples terá os valores conferidos. ODS com recursos não verificáveis será importado como cópia, conservando o original." };
        var encoding = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
        encoding.Items.AddRange(["Automático: UTF-8 / UTF-16 com BOM", "Windows-1252 (escolha explícita)"]); encoding.SelectedIndex = 0;
        var delimiter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
        delimiter.Items.AddRange(["CSV: detectar vírgula ou ponto e vírgula", "CSV: vírgula", "CSV: ponto e vírgula"]); delimiter.SelectedIndex = 0;
        layout.Controls.Add(extended); layout.Controls.Add(encoding); layout.Controls.Add(delimiter);
        try { var previous = ExtendedConfiguration.Load(LocalStorage.ForCurrentUser()); extended.Checked = true; extended.Enabled = false; encoding.Enabled = false; delimiter.Enabled = false; encoding.SelectedIndex = previous.Encoding == "auto" ? 0 : 1; delimiter.SelectedIndex = previous.Delimiter switch { "comma" => 1, "semicolon" => 2, _ => 0 }; }
        catch (LauncherNotConfiguredException) { }
        var xls = new CheckBox { AutoSize = true, MaximumSize = new Size(580, 0), Checked = XlsReplacementSettings.Load(LocalStorage.ForCurrentUser()), Text = "Substituir XLS por atalho após conferência (padrão)." };
        layout.Controls.Add(xls);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(580, 0), Text = "Macros não funcionam no Sheets; fórmulas, vínculos e formatação podem mudar. O original completo fica no backup. Desmarque a opção para conservar o arquivo local." });
        var saveXls = new Button { AutoSize = true, Text = "Salvar preferência XLS" };
        saveXls.Click += async (_, _) => { saveXls.Enabled = false; try { await XlsReplacementSettings.SaveAsync(LocalStorage.ForCurrentUser(), xls.Checked); status.Text = "Preferência XLS salva. Aplica-se às próximas aberturas e retomadas; arquivos já substituídos podem ser restaurados pelo backup."; } catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = LauncherErrors.Message(ex); } finally { saveXls.Enabled = true; } };
        layout.Controls.Add(saveXls);
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
                ExtendedConfiguration.Save(storage, new TextImportOptions(encoding.SelectedIndex == 0 ? "auto" : "windows-1252", delimiter.SelectedIndex switch { 1 => "comma", 2 => "semicolon", _ => "auto" }), extended.Checked);
                await XlsReplacementSettings.SaveAsync(storage, xls.Checked);
                new WindowsAssociationRegistration(Microsoft.Win32.Registry.CurrentUser).Register(Environment.ProcessPath!);
                WindowsAssociationRegistration.NotifyShell(); ExitCode = 0;
                status.Text = "Configuração salva. Feche esta tela, autorize o Google e escolha o aplicativo padrão na tela principal. Se uma planilha estava aguardando, abra-a novamente.";
            }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { ExitCode = 1; status.Text = "Confira o JSON desktop, a pasta dedicada e a declaração. Configurações existentes são preservadas; mudanças de cliente, pasta ou interpretação de texto exigem migração."; }
            finally { save.Enabled = true; }
        };
        layout.Controls.Add(save); layout.Controls.Add(status);
        layout.Controls.Add(new Button { Text = "Fechar", AutoSize = true, DialogResult = DialogResult.Cancel }); Controls.Add(layout);
        var policy = PilotSetup.PolicyPath(LocalStorage.ForCurrentUser()); if (File.Exists(policy)) folder.Text = File.ReadAllText(policy);
        Branding.Apply(this);
    }
}

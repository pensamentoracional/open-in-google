using SheetsWindows.Infrastructure;

namespace SheetsWindows.Windows;

internal sealed class SetupForm : Form
{
    public int ExitCode { get; private set; }
    private readonly TextBox client = new() { Width = 570, ReadOnly = true };
    private readonly TextBox folder = new() { Width = 570, ReadOnly = true };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(570, 0) };
    private readonly CancellationTokenSource cancellation = new();
    private bool busy;
    public SetupForm(bool firstUse = false, bool expanded = false)
    {
        var storage = LocalStorage.ForCurrentUser();
        Text = firstUse ? "Primeiro uso — ZagoSheetsWin" : "Configurações — ZagoSheetsWin";
        ClientSize = new Size(760, 760); MinimumSize = new Size(650, 620); AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        Label Info(string text) => new() { AutoSize = true, MaximumSize = new Size(570, 0), Text = text, Margin = new Padding(3, 6, 3, 6) };
        layout.Controls.Add(Info("Ao abrir uma planilha, o programa importa para o Sheets e substitui o original por um atalho após as verificações. O backup guarda o arquivo inicial completo, não as edições futuras no Google."));
        layout.Controls.Add(Info("1. Escolha uma pasta local de planilhas, fora de OneDrive e rede."));
        var chooseFolder = new Button { AutoSize = true, Text = "Escolher pasta…" };
        chooseFolder.Click += (_, _) => { using var dialog = new FolderBrowserDialog(); if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath; };
        layout.Controls.Add(chooseFolder); layout.Controls.Add(folder);
        var configured = !FirstUseState.NeedsSetup(storage);
        var policy = PilotSetup.PolicyPath(storage); if (File.Exists(policy)) { folder.Text = File.ReadAllText(policy); chooseFolder.Enabled = false; }
        var consent = new CheckBox { AutoSize = true, Checked = configured, Text = "Confirmo a pasta local e aceito a substituição com backup." };
        layout.Controls.Add(consent);
        var advanced = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = expanded, MaximumSize = new Size(590, 0) };
        var toggle = new Button { AutoSize = true, Text = expanded ? "Avançado ▾" : "Avançado ▸" };
        toggle.Click += (_, _) => { advanced.Visible = !advanced.Visible; toggle.Text = advanced.Visible ? "Avançado ▾" : "Avançado ▸"; };
        layout.Controls.Add(toggle); layout.Controls.Add(advanced);
        advanced.Controls.Add(Info("O aplicativo inclui o cliente OAuth Zagotools. Cada pessoa autoriza com sua própria conta Google. JSON próprio é opcional no primeiro uso; instalações configuradas preservam seu cliente."));
        var chooseClient = new Button { AutoSize = true, Text = "Escolher JSON OAuth…", Enabled = !File.Exists(LauncherConfiguration.ClientPath(storage)) };
        chooseClient.Click += (_, _) => { using var dialog = new OpenFileDialog { Filter = "JSON OAuth|*.json", CheckFileExists = true }; if (dialog.ShowDialog(this) == DialogResult.OK)  { client.Text = dialog.FileName; advanced.Visible = false; toggle.Text = "Avançado ▸"; status.Text = "JSON selecionado. Clique em Salvar e conectar Google."; } };
        advanced.Controls.Add(chooseClient); advanced.Controls.Add(client);
        if (File.Exists(LauncherConfiguration.ClientPath(storage))) client.Text = "Cliente OAuth já configurado — preservado.";
        var extended = new CheckBox { AutoSize = true, Checked = true, Text = "Habilitar CSV, TSV, XLS e ODS (experimental)." };
        var encoding = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 570 };
        encoding.Items.AddRange(["Texto: UTF-8 / UTF-16 com BOM", "Texto: Windows-1252"]); encoding.SelectedIndex = 0;
        var delimiter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 570 };
        delimiter.Items.AddRange(["CSV: detectar separador", "CSV: vírgula", "CSV: ponto e vírgula"]); delimiter.SelectedIndex = 0;
        advanced.Controls.Add(extended); advanced.Controls.Add(encoding); advanced.Controls.Add(delimiter);
        advanced.Controls.Add(Info("Até 20 MiB por arquivo; CSV/TSV: 500 mil células, 50 mil linhas e mil colunas. Texto literal; ODS não verificável conserva o original. Preferências de texto existentes são preservadas."));
        try { var previous = ExtendedConfiguration.Load(storage); extended.Enabled = false; encoding.Enabled = false; delimiter.Enabled = false; encoding.SelectedIndex = previous.Encoding == "auto" ? 0 : 1; delimiter.SelectedIndex = previous.Delimiter switch { "comma" => 1, "semicolon" => 2, _ => 0 }; }
        catch (LauncherNotConfiguredException) { }
        var xls = new CheckBox { AutoSize = true, Checked = XlsReplacementSettings.Load(storage), Text = "Substituir XLS por atalho após conferência." };
        advanced.Controls.Add(xls);
        advanced.Controls.Add(Info("Macros não funcionam no Sheets. Fórmulas, vínculos e formatação podem mudar. O backup conserva o original completo. Desmarque para importar XLS como cópia."));
        var saveXls = new Button { AutoSize = true, Text = "Salvar preferência XLS" };
        saveXls.Click += async (_, _) => { saveXls.Enabled = false; try { await XlsReplacementSettings.SaveAsync(storage, xls.Checked); status.Text = "Preferência XLS salva."; } catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = LauncherErrors.Message(ex); } finally { saveXls.Enabled = true; } };
        advanced.Controls.Add(saveXls);
        layout.Controls.Add(Info("2. Conecte sua própria conta Google. As planilhas ficam no seu Drive; a autorização acontece no navegador."));
        var connect = new Button { AutoSize = true, Text = "Salvar e conectar Google" };
        var save = new Button { AutoSize = true, Text = "Salvar configurações" };
        var defaults = new Button { AutoSize = true, Text = "Abrir Aplicativos padrão do Windows" };
        var finish = new Button { AutoSize = true, Text = firstUse ? "Concluir" : "Fechar" };
        var cancel = new Button { AutoSize = true, Text = "Cancelar conexão", Visible = false };
        cancel.Click += (_, _) => cancellation.Cancel();
        bool Ready() => !FirstUseState.NeedsAuthorization(storage);
        if (firstUse) finish.Enabled = Ready();
        async Task Save(bool authorize)
        {
            if (busy) return;
            busy = true; layout.Enabled = false; cancel.Visible = authorize; cancel.Enabled = true; status.Text = authorize ? "Salvando e aguardando autorização no navegador…" : "Salvando…";
            try
            {
                await using (var held = await new FileOperationLock(storage.LocksPath).AcquireAsync("windows-registration", cancellation.Token))
                {
                    var json = await LauncherConfiguration.SetupClientJsonAsync(storage, client.Text, cancellation.Token);
                    PilotSetup.Configure(storage, json, folder.Text, consent.Checked);
                    ExtendedConfiguration.Save(storage, new(encoding.SelectedIndex == 0 ? "auto" : "windows-1252", delimiter.SelectedIndex switch { 1 => "comma", 2 => "semicolon", _ => "auto" }), extended.Checked);
                    await XlsReplacementSettings.SaveAsync(storage, xls.Checked, cancellation.Token);
                    new WindowsAssociationRegistration(Microsoft.Win32.Registry.CurrentUser).Register(Environment.ProcessPath!); WindowsAssociationRegistration.NotifyShell();
                }
                if (authorize)
                {
                    using var handler = new HttpClientHandler { AllowAutoRedirect = false }; using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
                    await Task.Run(() => new WindowsLauncher(storage, http, new BrowserLauncher()).LoginAsync(cancellation.Token));
                }
                ExitCode = 0; status.Text = authorize ? "Google conectado. Escolha ZagoSheetsWin para os formatos no Windows e volte aqui para concluir." : "Configurações salvas; cliente, pasta e preferências anteriores foram preservados.";
                if (firstUse) finish.Enabled = Ready();
            }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { ExitCode = 1; status.Text = ex is ArgumentException ? "Escolha a pasta e marque a confirmação de substituição com backup." : LauncherErrors.Message(ex); }
            finally { busy = false; layout.Enabled = true; cancel.Visible = false; if (cancellation.IsCancellationRequested) connect.Enabled = save.Enabled = false; }
        }
        connect.Click += async (_, _) => await Save(true); save.Click += async (_, _) => await Save(false);
        layout.Controls.Add(connect); if (!firstUse) layout.Controls.Add(save);
        layout.Controls.Add(Info("3. No Windows, procure ZagoSheetsWin e escolha os formatos desejados. Depois volte e clique em Concluir. A instalação não altera seus aplicativos padrão automaticamente."));
        defaults.Click += (_, _) => { try { new BrowserLauncher().Open(WindowsAssociationPlan.DefaultsUri); } catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = "Abra Configurações > Aplicativos > Aplicativos padrão e procure ZagoSheetsWin."; } };
        layout.Controls.Add(defaults); layout.Controls.Add(status); layout.Controls.Add(finish);
        finish.Click += (_, _) => Close();
        Controls.Add(layout); Controls.Add(cancel); cancel.Dock = DockStyle.Bottom;
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; cancellation.Cancel(); } };
        Branding.Apply(this);
    }
    protected override void Dispose(bool disposing) { if (disposing) cancellation.Dispose(); base.Dispose(disposing); }
}

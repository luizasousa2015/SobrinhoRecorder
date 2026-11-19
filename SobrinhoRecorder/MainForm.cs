using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SobrinhoRecorder;

public sealed class MainForm : Form
{
    private readonly Button startButton = new();
    private readonly Button pauseButton = new();
    private readonly Button stopButton = new();
    private readonly Label statusLabel = new();
    private readonly Label timerLabel = new();
    private readonly Label fileLabel = new();
    private readonly System.Windows.Forms.Timer clock = new() { Interval = 250 };
    private readonly MiniPanel mini = new();
    private Process? ffmpeg;
    private DateTime startedAt;
    private TimeSpan elapsedBeforePause = TimeSpan.Zero;
    private bool isPaused;
    private bool closing;
    private string? currentOutput;

    public MainForm()
    {
        Text = "Sobrinho Recorder — versão de teste";
        MinimumSize = new Size(620, 430);
        Size = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(16, 25, 39);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);

        var title = new Label { Text = "SOBRINHO RECORDER", Font = new Font("Segoe UI", 20F, FontStyle.Bold), ForeColor = Color.FromArgb(58, 198, 222), AutoSize = true, Location = new Point(28, 22) };
        var subtitle = new Label { Text = "Gravador de tela para Windows · versão experimental", ForeColor = Color.LightGray, AutoSize = true, Location = new Point(31, 62) };
        var preview = new Panel { Location = new Point(28, 105), Size = new Size(688, 190), BackColor = Color.FromArgb(27, 43, 62), BorderStyle = BorderStyle.FixedSingle };
        var previewIcon = new Label { Text = "▣", Font = new Font("Segoe UI Symbol", 42F), ForeColor = Color.FromArgb(58, 198, 222), AutoSize = true };
        var previewText = new Label { Text = "CAPTURA DA TELA\nA gravação será salva em MP4", ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter, AutoSize = true, Font = new Font("Segoe UI", 12F) };
        preview.Controls.Add(previewIcon); preview.Controls.Add(previewText);
        preview.Resize += (_, _) => { previewIcon.Location = new Point((preview.Width - previewIcon.Width) / 2, 34); previewText.Location = new Point((preview.Width - previewText.Width) / 2, 105); };

        timerLabel.Text = "00:00:00"; timerLabel.Font = new Font("Consolas", 22F, FontStyle.Bold); timerLabel.ForeColor = Color.White; timerLabel.AutoSize = true; timerLabel.Location = new Point(30, 315);
        statusLabel.Text = "● PRONTO PARA GRAVAR"; statusLabel.ForeColor = Color.FromArgb(100, 210, 150); statusLabel.AutoSize = true; statusLabel.Location = new Point(190, 327);
        fileLabel.Text = "O arquivo será salvo na pasta Vídeos"; fileLabel.ForeColor = Color.Silver; fileLabel.AutoSize = true; fileLabel.Location = new Point(31, 360);

        StyleButton(startButton, "●  INICIAR GRAVAÇÃO", Color.FromArgb(190, 47, 63)); startButton.Location = new Point(30, 405); startButton.Size = new Size(210, 42); startButton.Click += (_, _) => StartRecording();
        StyleButton(pauseButton, "Ⅱ  PAUSAR", Color.FromArgb(42, 62, 83)); pauseButton.Location = new Point(252, 405); pauseButton.Size = new Size(140, 42); pauseButton.Enabled = false; pauseButton.Click += (_, _) => TogglePause();
        StyleButton(stopButton, "■  SALVAR / STOP", Color.FromArgb(42, 62, 83)); stopButton.Location = new Point(404, 405); stopButton.Size = new Size(180, 42); stopButton.Enabled = false; stopButton.Click += async (_, _) => await FinishRecording();

        Controls.AddRange([title, subtitle, preview, timerLabel, statusLabel, fileLabel, startButton, pauseButton, stopButton]);
        clock.Tick += (_, _) => UpdateElapsed(); clock.Start();
        mini.Owner = this;
        mini.PauseClicked += (_, _) => TogglePause();
        mini.ResumeClicked += (_, _) => TogglePause();
        mini.SaveClicked += async (_, _) => await FinishRecording();
        mini.StopClicked += async (_, _) => await FinishRecording();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized && IsRecording) ShowMini(); else if (WindowState != FormWindowState.Minimized) mini.Hide(); };
        FormClosing += async (_, e) =>
        {
            if (closing) return;
            if (IsRecording)
            {
                e.Cancel = true;
                var answer = MessageBox.Show("Há uma gravação em andamento. Deseja finalizar e salvar antes de sair?", "Sobrinho Recorder", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.Yes) await FinishRecording(); else KillRecorder();
            }
            closing = true; mini.Close();
        };
    }

    private bool IsRecording => ffmpeg is { HasExited: false };

    private static void StyleButton(Button b, string text, Color color)
    {
        b.Text = text; b.BackColor = color; b.ForeColor = Color.White; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.Font = new Font("Segoe UI", 9F, FontStyle.Bold); b.Cursor = Cursors.Hand;
    }

    private void StartRecording()
    {
        string ffmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (!File.Exists(ffmpegPath))
        {
            MessageBox.Show("Não encontrei ffmpeg.exe. Coloque o executável do FFmpeg na mesma pasta do SobrinhoRecorder.exe. Consulte o LEIA-ME.", "FFmpeg necessário", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        string folder = Path.Combine(videos, "Sobrinho Recorder"); Directory.CreateDirectory(folder);
        currentOutput = Path.Combine(folder, $"Gravacao_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4");
        var psi = new ProcessStartInfo(ffmpegPath)
        {
            Arguments = $"-hide_banner -y -f gdigrab -framerate 30 -i desktop -c:v libx264 -preset ultrafast -pix_fmt yuv420p -movflags +faststart \"{currentOutput}\"",
            UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory
        };
        try
        {
            ffmpeg = Process.Start(psi);
            if (ffmpeg == null) throw new InvalidOperationException("O FFmpeg não iniciou.");
            startedAt = DateTime.Now; elapsedBeforePause = TimeSpan.Zero; isPaused = false;
            startButton.Enabled = false; pauseButton.Enabled = true; stopButton.Enabled = true;
            pauseButton.Text = "Ⅱ  PAUSAR"; statusLabel.Text = "● GRAVANDO"; statusLabel.ForeColor = Color.IndianRed;
            fileLabel.Text = "Salvando: " + Path.GetFileName(currentOutput);
            mini.SetState("REC", "00:00:00", false); UpdateElapsed();
        }
        catch (Exception ex) { MessageBox.Show("Não foi possível iniciar a gravação:\n" + ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void TogglePause()
    {
        if (!IsRecording) return;
        try
        {
            if (!isPaused)
            {
                elapsedBeforePause += DateTime.Now - startedAt;
                NativeMethods.NtSuspendProcess(ffmpeg!.Handle);
                isPaused = true; pauseButton.Text = "▶  CONTINUAR"; statusLabel.Text = "Ⅱ PAUSADO"; statusLabel.ForeColor = Color.Gold;
                mini.SetState("PAUSE", timerLabel.Text, true);
            }
            else
            {
                NativeMethods.NtResumeProcess(ffmpeg!.Handle);
                startedAt = DateTime.Now; isPaused = false; pauseButton.Text = "Ⅱ  PAUSAR"; statusLabel.Text = "● GRAVANDO"; statusLabel.ForeColor = Color.IndianRed;
                mini.SetState("REC", timerLabel.Text, false);
            }
        }
        catch (Exception ex) { MessageBox.Show("Não foi possível alterar o estado da gravação: " + ex.Message); }
    }

    private async Task FinishRecording()
    {
        if (!IsRecording) return;
        var proc = ffmpeg!;
        try
        {
            if (isPaused) { NativeMethods.NtResumeProcess(proc.Handle); isPaused = false; }
            await proc.StandardInput.WriteLineAsync("q");
            await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));
        }
        catch
        {
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
        }
        ffmpeg = null; isPaused = false;
        startButton.Enabled = true; pauseButton.Enabled = false; stopButton.Enabled = false;
        pauseButton.Text = "Ⅱ  PAUSAR"; statusLabel.Text = "✓ GRAVAÇÃO FINALIZADA"; statusLabel.ForeColor = Color.LightGreen;
        mini.Hide(); WindowState = FormWindowState.Normal; Show(); Activate();
        if (currentOutput != null && File.Exists(currentOutput))
        {
            fileLabel.Text = "Vídeo salvo: " + currentOutput;
            MessageBox.Show("Gravação finalizada e salva em:\n" + currentOutput, "Sobrinho Recorder", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else { fileLabel.Text = "A gravação não gerou um arquivo válido. Verifique o FFmpeg."; }
    }

    private void UpdateElapsed()
    {
        if (!IsRecording) return;
        TimeSpan elapsed = elapsedBeforePause + (isPaused ? TimeSpan.Zero : DateTime.Now - startedAt);
        string value = elapsed.ToString(@"hh\:mm\:ss"); timerLabel.Text = value;
        mini.SetTime(value);
    }

    private void ShowMini()
    {
        mini.SetTime(timerLabel.Text); mini.Show(); mini.Location = new Point(Math.Max(0, Screen.FromControl(this).WorkingArea.Right - mini.Width - 24), Math.Max(0, Screen.FromControl(this).WorkingArea.Bottom - mini.Height - 24)); mini.TopMost = true;
    }

    private void KillRecorder()
    {
        try { if (IsRecording) ffmpeg!.Kill(true); } catch { }
        ffmpeg = null; mini.Hide();
    }
}

internal sealed class MiniPanel : Form
{
    private readonly Label time = new() { AutoSize = false, Text = "00:00:00", ForeColor = Color.White, Font = new Font("Consolas", 9F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Location = new Point(4, 1), Size = new Size(59, 17) };
    private readonly Label state = new() { AutoSize = false, Text = "REC", ForeColor = Color.FromArgb(255, 95, 105), Font = new Font("Segoe UI", 7F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight, Location = new Point(88, 1), Size = new Size(27, 17) };
    private readonly Waveform wave = new() { Location = new Point(64, 4), Size = new Size(23, 12) };
    public event EventHandler? SaveClicked; public event EventHandler? PauseClicked; public event EventHandler? ResumeClicked; public event EventHandler? StopClicked;
    private bool paused;

    public MiniPanel()
    {
        FormBorderStyle = FormBorderStyle.None; Size = new Size(120, 50); ClientSize = new Size(120, 50); TopMost = true; ShowInTaskbar = false; BackColor = Color.FromArgb(15, 25, 39); StartPosition = FormStartPosition.Manual;
        var save = MakeButton("S", 3, Color.FromArgb(43, 65, 86), "Finalizar e salvar"); save.Click += (_, _) => SaveClicked?.Invoke(this, EventArgs.Empty);
        var pause = MakeButton("Ⅱ", 32, Color.FromArgb(43, 65, 86), "Pausar / continuar"); pause.Click += (_, _) => { if (paused) ResumeClicked?.Invoke(this, EventArgs.Empty); else PauseClicked?.Invoke(this, EventArgs.Empty); paused = !paused; pause.Text = paused ? "▶" : "Ⅱ"; };
        var stop = MakeButton("■", 61, Color.FromArgb(43, 65, 86), "Stop — finalizar gravação"); stop.Click += (_, _) => StopClicked?.Invoke(this, EventArgs.Empty);
        var restore = MakeButton("↗", 90, Color.FromArgb(29, 145, 169), "Restaurar janela principal"); restore.Click += (_, _) => { Hide(); if (Owner != null) { Owner.WindowState = FormWindowState.Normal; Owner.Show(); Owner.Activate(); } };
        Controls.AddRange([time, state, wave, save, pause, stop, restore]);
        MouseDown += Drag; foreach (Control c in Controls) { c.MouseDown += Drag; }
    }

    private static Button MakeButton(string text, int x, Color color, string tip)
    {
        var b = new Button { Text = text, Location = new Point(x, 23), Size = new Size(26, 23), BackColor = color, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8F, FontStyle.Bold), Cursor = Cursors.Hand, TabStop = false };
        b.FlatAppearance.BorderSize = 0; new ToolTip().SetToolTip(b, tip); return b;
    }
    public void SetTime(string value) => time.Text = value;
    public void SetState(string value, string timeValue, bool isPaused) { state.Text = value; time.Text = timeValue; paused = isPaused; }
    private void Drag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        NativeMethods.ReleaseCapture(); NativeMethods.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
    }
}

internal sealed class Waveform : Control
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 110 };
    private readonly Random random = new(); private int[] levels = new int[7];
    public Waveform() { DoubleBuffered = true; timer.Tick += (_, _) => { for (int i = 0; i < levels.Length; i++) levels[i] = random.Next(2, 12); Invalidate(); }; timer.Start(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); using var brush = new SolidBrush(Color.FromArgb(47, 197, 218));
        for (int i = 0; i < levels.Length; i++) e.Graphics.FillRectangle(brush, i * 3 + 1, (Height - levels[i]) / 2, 2, levels[i]);
    }
}

internal static class NativeMethods
{
    [DllImport("ntdll.dll")] internal static extern int NtSuspendProcess(IntPtr processHandle);
    [DllImport("ntdll.dll")] internal static extern int NtResumeProcess(IntPtr processHandle);
    [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}

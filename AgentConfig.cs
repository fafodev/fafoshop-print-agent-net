namespace FafoshopPrintAgentNet;

/// <summary>
/// Cấu hình agent — đọc từ <c>agent.properties</c> nằm CÙNG thư mục với file
/// thực thi (nếu có), fallback về mặc định nếu không có file hoặc thiếu key.
/// Cho phép đổi domain frontend production sau này mà KHÔNG cần build lại
/// agent. Giữ NGUYÊN format key=value như bản Java tham chiếu
/// (<c>fafoshop-print-agent/.../PrintAgentMain.java</c>, class AgentConfig) —
/// 1 file <c>agent.properties</c> dùng chung được cho cả 2 bản nếu cần, và dễ
/// đối chiếu hành vi khi vận hành song song.
/// </summary>
internal sealed class AgentConfig
{
    public int Port { get; }
    public List<string> AllowedOrigins { get; }
    public bool DebugSaveLastPrint { get; }

    private AgentConfig(int port, List<string> allowedOrigins, bool debugSaveLastPrint)
    {
        Port = port;
        AllowedOrigins = allowedOrigins;
        DebugSaveLastPrint = debugSaveLastPrint;
    }

    public static AgentConfig Load()
    {
        int port = 9199;
        var origins = new List<string> { "http://localhost:4200" };
        bool debugSaveLastPrint = false;

        string configPath = Path.Combine(AppContext.BaseDirectory, "agent.properties");
        if (File.Exists(configPath))
        {
            try
            {
                foreach (string rawLine in File.ReadAllLines(configPath))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('!'))
                    {
                        continue; // dòng trống/comment — giống Properties.load() của Java.
                    }

                    int eq = line.IndexOf('=');
                    if (eq < 0)
                    {
                        continue;
                    }

                    string key = line[..eq].Trim();
                    string value = line[(eq + 1)..].Trim();

                    switch (key)
                    {
                        case "port":
                            if (int.TryParse(value, out int parsedPort))
                            {
                                port = parsedPort;
                            }
                            break;
                        case "allowedOrigins":
                            origins = value
                                .Split(',')
                                .Select(o => o.Trim())
                                .Where(o => o.Length > 0)
                                .ToList();
                            break;
                        case "debugSaveLastPrint":
                            debugSaveLastPrint = bool.TryParse(value, out bool parsedDebug) && parsedDebug;
                            break;
                    }
                }

                Console.WriteLine($"[print-agent] Config:Loaded {configPath}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[print-agent] Config:LoadFailed {e} — dùng mặc định.");
            }
        }
        else
        {
            Console.WriteLine("[print-agent] Config:UsingDefault (không thấy agent.properties)");
        }

        return new AgentConfig(port, origins, debugSaveLastPrint);
    }
}

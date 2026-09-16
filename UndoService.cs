namespace W1sleyEdition;

public sealed class UndoService(PowerModeService powerModes, StartupManagerService startup, NetworkOptimizationService network, ActionLogger logger)
{
    private readonly PowerModeService _powerModes = powerModes;
    private readonly StartupManagerService _startup = startup;
    private readonly NetworkOptimizationService _network = network;
    private readonly ActionLogger _logger = logger;

    public async Task UndoAsync(ActionLogEntry entry)
    {
        if (!entry.CanUndo || entry.UndoData is null) throw new InvalidOperationException("Bu işlem otomatik geri alınamaz.");
        switch (entry.UndoType)
        {
            case "power-state":
                if (!entry.UndoData.TryGetValue("boost", out var boostText) || !int.TryParse(boostText, out var boost) ||
                    !entry.UndoData.TryGetValue("maximumFrequency", out var maximumFrequencyText) || !int.TryParse(maximumFrequencyText, out var maximumFrequency))
                    throw new InvalidOperationException("Önceki AC güç durumu kaydı geçersiz.");
                await _powerModes.RestoreStateAsync(new PowerStateSnapshot(boost, maximumFrequency));
                break;
            case "power-mode":
                if (!entry.UndoData.TryGetValue("mode", out var modeText) || !int.TryParse(modeText, out var modeValue))
                    throw new InvalidOperationException("Önceki güç modu kaydı geçersiz.");
                await _powerModes.SetModeAsync((PowerMode)modeValue, false);
                break;
            case "startup-enable":
                _startup.RestoreWithoutLogging(entry.UndoData["name"], entry.UndoData["command"]);
                break;
            case "startup-disable":
                _startup.DisableWithoutLogging(entry.UndoData["name"], entry.UndoData["command"]);
                break;
            case "network-latency":
                if (!entry.UndoData.TryGetValue("snapshot", out var latencySnapshot) ||
                    !await _network.RestoreLatencyOptimizationAsync(latencySnapshot))
                    throw new InvalidOperationException("Ağ gecikme ayarı yedeği geri yüklenemedi.");
                break;
            case "network-dns":
                if (!entry.UndoData.TryGetValue("snapshot", out var dnsSnapshot) ||
                    !await _network.RestoreDnsStateAsync(dnsSnapshot))
                    throw new InvalidOperationException("DNS ayarı yedeği geri yüklenemedi.");
                break;
            default:
                throw new InvalidOperationException("Bu işlem için geri alma yöntemi bulunamadı.");
        }
        await _logger.LogAsync(new ActionLogEntry { Action = "Geri alındı", Detail = $"{entry.Action}: {entry.Detail}", Success = true });
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

#region Server Communication Models

[Serializable]
public class InitData
{
    public string id = "initData";
    public ServerGameData gameData;
    public ServerFeatures features;
    public ServerUIData uiData;
    public ServerPlayer player;
    public JackpotData jackpotData;
}

[Serializable]
public class JackpotData
{
    public JackpotValues values;//jackpotfeature

}

[Serializable]
public class JackpotValues
{
    public string miniJackpot;
    public string minorJackpot;
    public string majorJackpot;
    public string grandJackpot;
}

[Serializable]
public class JackpotSyncData
{
    public string gameId;
    public JackpotValues values;
}

[Serializable]
public class ServerGameData
{
    public List<List<int>> lines;
    public List<double> bets;
    public int totalLines;
    public int activeLine;
}

[Serializable]
public class ServerFeatures
{
    public ServerScatterFeature scatter;
    public ServerFreeGamesFeature freeGames;
    public ServerGroupPayoutFeature anyBarsPayout;
    public ServerGroupPayoutFeature anySevensGroup;
    public ServerWildSubstitutionFeature wildSubstitution;
}

[Serializable]
public class ServerScatterFeature
{
    public double payout;
    public bool enabled;
    public int minTriggerCount;
    public int scatterSymbolId;
}

[Serializable]
public class ServerFreeGamesFeature
{
    public List<ServerFreeGamesBox> boxes;
    public bool enabled;
}

[Serializable]
public class ServerFreeGamesBox
{
    public string id;
    public double prob;
    public int spins;
    public List<double> multipliers;
}

[Serializable]
public class ServerGroupPayoutFeature
{
    public double payout;
    public bool enabled;
    public string groupName;
}

[Serializable]
public class ServerWildSubstitutionFeature
{
    public bool enabled;
    public int wildSymbolId;
    public List<int> substituteAllExcept;
}

// Dormant CNY-era feature models — no longer referenced by ServerFeatures, kept only because
// GameConfig.uSpinSegments (also dormant) still uses USpinSegment's type.
[Serializable]
public class USpinFeature
{
    public bool enabled;
    public int minTrigger;
    public int symbolId;
    public List<USpinSegment> segments;
}

[Serializable]
public class USpinSegment
{
    public int sliceIndex;
    public string type;
    public double multiplier;
    public int freeGames;
}

[Serializable]
public class MoneyBagFeature
{
    public bool enabled;
    public int minTrigger;
    public int symbolId;
    public int bagCount;
}

[Serializable]
public class ExtraSpinsData
{
    [JsonProperty("2")] public int _2; // Keep for safety/compatibility with UI
    [JsonProperty("3")] public int _3;
    [JsonProperty("4")] public int _4;
    [JsonProperty("5")] public int _5;
}

[Serializable]
public class ServerUIData
{
    public PaylineData paylines;
}

[Serializable]
public class PaylineData
{
    public List<ServerSymbolInfo> symbols;
}

[Serializable]
public class ServerSymbolInfo
{
    public int id;
    public string name;
    public List<double> multiplier; // Keep for fallback compatibility
    public double payout;           // Single flat payout per symbol (3-reel fixed-payline game — no per-match-count table)
    public string description;
    public int minMatch;
    public string group;
}


[Serializable]
public class ServerPlayer
{
    public double balance;
}

// ============================================================================
// Server Response Models — real Sizzling7 backend shape
// ============================================================================

[Serializable]
public class ServerSpinResponse
{
    public string id = "ResultData";
    public bool success;
    public ServerPlayerBalance player;
    public ServerPayload payload;
}

[Serializable]
public class ServerPlayerBalance
{
    public double? balance; // Nullable because server sends null
}

[Serializable]
public class ServerPayload
{
    public List<List<string>> reels;              // Row-major: 5 rows (0-4, rows 1-3 active) x reelCount columns
    public List<ServerWinningLine> winningLines;
    public double totalWin;
    public bool isFreeSpin;                        // True when this spin was played on free-spin credit
    public int freeSpinsRemaining;                 // Server-authoritative; already decremented for this spin
    // Nullable on purpose: the server omits this entirely on spins it doesn't apply to, and a
    // missing value must never be read as a real multiplier of 0.
    public double? freeSpinsMultiplier;
    public ServerResultFeatures features;
}

[Serializable]
public class ServerWinningLine
{
    public int lineIndex;
    public List<List<int>> positions;              // [row, col] pairs, row in the padded 0-4 space
    public double payout;
    public bool isAnyBars;
    public bool isAnySevens;                        // Not yet observed in captured data, modeled defensively
}

[Serializable]
public class ServerResultFeatures
{
    public ServerBonusResult bonus;
    public bool freeGamesTriggered;
    public int awardedSpins;
    public string boxId;                            // Which mystery box the server picked ("red"/"yellow"/"blue"/"green")
    // Running total for the free-games round. Nullable so an absent field can't be read as a real
    // 0 and blank out a round that has already paid — the same trap freeSpinsMultiplier avoids.
    public double? freeSpinTotalWin;
}

// The bonus/scatter symbol landing that triggers free games. JSON key is "bonus".
[Serializable]
public class ServerBonusResult
{
    public bool triggered;
    public int bonusCount;
    public List<List<int>> positions;               // [row, col] pairs, row in the padded 0-4 space
    public double award;                            // Cash paid by the bonus symbols themselves
}

// Dormant CNY-era result models — no longer referenced by ServerPayload, kept because
// SpinResult.uSpinData/moneyBagData (also dormant) still use these detail types.
[Serializable]
public class ServerUSpinResult
{
    public bool triggered;
    public ServerUSpinResultDetail result;
}

[Serializable]
public class ServerUSpinResultDetail
{
    public int sliceIndex;
    public string type;
    public double multiplierAwarded;
    public int freeGamesAwarded;
    public double winInCash;
}

[Serializable]
public class ServerMoneyBagResult
{
    public bool triggered;
    public ServerMoneyBagResultDetail result;
}

[Serializable]
public class ServerMoneyBagResultDetail
{
    public int pickedIndex;
    public List<int> revealed;
    public int creditsAwarded;
    public double winInCash;
}

[Serializable]
public class ServerFreeGamesResult
{
    public bool triggered;
    public int totalAwarded;
    public int played;
    public double totalFreeGamesWin;
}

// ============================================================================
// Client-Side Spin Request
// ============================================================================

[Serializable]
public class SpinRequest
{
    public string type = "SPIN";
    public SpinPayload payload;
}

[Serializable]
public class SpinPayload
{
    public int betIndex;
    public bool isFreeSpin;
}




#endregion

#region Game Configuration (Client Side Converted)

[Serializable]
public class GameConfig
{
    public int reelCount = 3;
    public int rowCount = 3;               // Active rows per reel (the server sends 5 total per reel; rows 0/4 are decorative padding, not represented here)
    public int totalResponseRowCount = 5;  // Total rows per reel in the raw server response, including decorative padding
    public int symbolCount = 8;
    public int paylineCount = 27;
    public int activeLine = 1;             // Number of active paylines — this IS the per-line-bet multiplier for total bet
    public List<List<int>> paylines;
    public List<double> availableBets;
    public List<SymbolInfo> symbols;

    // Wild configuration
    public int wildSymbolId = 1;       // 2xWild (1)

    // Scatter configuration
    public int scatterSymbolId = 0;    // Bonus is ID 0

    // Blank/filler configuration. Defaults to 7 to match the current symbol table, but is
    // overwritten from the server's "blank" group in ConvertToGameConfig.
    public int blankSymbolId = 7;

    public int betMultiplier = 1;          // Unused by any real gameplay calc — kept only for UI-compile safety
    public int maxWinMultiplier = 10000;   // Unused by any real gameplay calc — kept only for UI-compile safety
    public int minWinMultiplier = 10;      // Unused by any real gameplay calc — kept only for UI-compile safety
    public int initialFreeSpins = 12;      // Unused by any real gameplay calc — kept only for UI-compile safety
    public ExtraSpinsData extraSpinsData; // Keep to avoid compilation error in UI

    // Dormant — no longer populated from real server data (see InitDataConverter), kept for UI-compile safety
    public List<USpinSegment> uSpinSegments;
}

[Serializable]
public class SymbolInfo
{
    public int id;
    public string name;
    public List<double> multipliers;
    public bool isWild;
    public bool isScatter;
    // Filler symbol — never pays, never animates, and is not clickable for an info card.
    public bool isBlank;
    public int wildMultiplier = 1;
    public int minMatch;
}

#endregion

#region Player & Game State (Client Side)

[Serializable]
public class PlayerData
{
    public double balance;
    public int currentBetIndex;
}

[Serializable]
public class SpinResult
{
    public List<List<int>> resultMatrix;  // Client uses int matrix
    public double winAmount;
    public double grandTotalWin;
    public List<WinLine> winLines;
    public PlayerData playerData;
    public FreeSpinData freeSpinData;
    public ScatterData scatterData;
    public OverlayScatterData overlayScatterData; // Keep for safety/UI compilation
    public Dictionary<string, int> stickyWilds;  // Keep for safety/UI compilation

    // Per-response free spin facts. Spins used and total awarded are still GameManager's to track —
    // the wire format doesn't carry them — but the round's total win now comes from the server.
    public int serverSpinsRemaining;
    public bool isRoundOver;
    // Informational only — the server has already applied this to winAmount. Never multiply by it.
    public double? freeSpinsMultiplier;
    // The round's running total, server-authoritative. Replaced client-side accumulation, which
    // drifted whenever a response was missed or the socket reconnected mid-round.
    public double? serverFreeSpinTotalWin;

    // Dormant — CNY-era wheel/moneybag features, never populated from real server data
    public USpinResultData uSpinData;
    public MoneyBagResultData moneyBagData;

    public double GetMoneyBagWin()
    {
        return (moneyBagData != null && moneyBagData.triggered) ? moneyBagData.winInCash : 0;
    }

    public double GetUSpinCashWin()
    {
        return (uSpinData != null && uSpinData.triggered && uSpinData.type == "MULTIPLIER") ? uSpinData.winInCash : 0;
    }

    public double GetTotalFeatureDeferredWins()
    {
        return GetMoneyBagWin() + GetUSpinCashWin();
    }
}

[Serializable]
public class WinLine
{
    public int lineId;
    public int symbolId;
    public List<int> positions;  // Flat list: [row * reelCount + col], row in local active-row space (0..rowCount-1)
    public double winAmount;
}

[Serializable]
public class FreeSpinData
{
    public bool isTriggered;
    public int spinsAwarded;
    public int remainingSpins;
    public bool isBought;
    public string boxId;   // Which mystery box the server picked; presentation only
}

[Serializable]
public class ScatterData
{
    public bool isTriggered;
    public int scatterCount;
    public double winAmount;
}

[Serializable]
public class OverlayScatterData
{
    public bool isTriggered;
    public int count;
    public int extraSpins;
    public List<List<int>> positions;
}

[Serializable]
public class USpinResultData
{
    public bool triggered;
    public int sliceIndex;
    public string type;
    public double multiplierAwarded;
    public int freeGamesAwarded;
    public double winInCash;
}

[Serializable]
public class MoneyBagResultData
{
    public bool triggered;
    public int pickedIndex;
    public List<int> revealed;
    public int creditsAwarded;
    public double winInCash;
}

#endregion

#region Platform Communication

[Serializable]
public class AuthData
{
    public string token;
    public string socketURL;
    public string nameSpace;
}

#endregion

#region Enums

public enum GameState
{
    Initializing,
    Idle,
    Spinning,
    Stopping,
    ShowingWin,
    FreeSpinMode
}

public enum SpinSpeed
{
    Normal,
    Turbo,
    QuickSpin
}

public enum WinPopupType
{
    BigWin              // Win at or above GameManager.bigWinMultiplierThreshold
    // Free-games trigger/complete presentation lives in FreeGameView, not this popup.
}

#endregion

#region Helper Classes for Conversion

/// <summary>
/// Converts server data to client GameConfig
/// </summary>
public static class InitDataConverter
{
    internal static GameConfig ConvertToGameConfig(InitData serverData)
    {
        int derivedReelCount = (serverData.gameData?.lines != null && serverData.gameData.lines.Count > 0 && serverData.gameData.lines[0] != null)
            ? serverData.gameData.lines[0].Count
            : 3;

        var config = new GameConfig
        {
            reelCount = derivedReelCount,
            rowCount = 3,
            symbolCount = serverData.uiData.paylines.symbols.Count,
            paylineCount = serverData.gameData.totalLines,
            activeLine = serverData.gameData.activeLine,
            paylines = serverData.gameData.lines,
            availableBets = serverData.gameData.bets,
            symbols = new List<SymbolInfo>()
        };

        foreach (var serverSymbol in serverData.uiData.paylines.symbols)
        {
            var symbolInfo = new SymbolInfo
            {
                id = serverSymbol.id,
                name = serverSymbol.name,
                multipliers = new List<double>(),
                isWild = serverSymbol.group == "wild",
                // The server calls the free-games trigger symbol group "bonus"; "scatter" is the
                // CNY-era name this field still carries. Matching the wrong string left isScatter
                // permanently false, so SymbolInfoCard rendered Bonus as an ordinary symbol with a
                // 0 payout instead of its "3 or more trigger Free Games" text, and scatterSymbolId
                // was never assigned — it only held the right id because its default happens to be 0.
                isScatter = serverSymbol.group == "bonus",
                isBlank = serverSymbol.group == "blank",
                minMatch = serverSymbol.minMatch
            };

            // Store the flat payout value for the info page (3-reel fixed-payline game — one payout per symbol, not a per-match-count table)
            symbolInfo.multipliers.Add(serverSymbol.payout);
            config.symbols.Add(symbolInfo);

            if (symbolInfo.isWild)
            {
                config.wildSymbolId = symbolInfo.id;
            }
            if (symbolInfo.isScatter)
            {
                config.scatterSymbolId = symbolInfo.id;
            }
            if (symbolInfo.isBlank)
            {
                config.blankSymbolId = symbolInfo.id;
            }
        }

        return config;
    }

    internal static PlayerData ConvertToPlayerData(ServerPlayer serverPlayer, int defaultBetIndex = 0)
    {
        return new PlayerData
        {
            balance = serverPlayer.balance,
            currentBetIndex = defaultBetIndex
        };
    }

    /// <summary>
    /// Converts server response to client SpinResult
    /// </summary>
    internal static SpinResult ConvertServerResponseToSpinResult(ServerSpinResponse serverResponse, double currentBalance, double betAmount, GameConfig gameConfig)
    {
        double winAmountVal = serverResponse.payload.totalWin;
        double activeLine = (gameConfig != null && gameConfig.activeLine > 0) ? gameConfig.activeLine : 27;
        double totalPay = betAmount * activeLine;
        double newBalance = serverResponse.player?.balance ?? CalculateNewBalance(currentBalance, totalPay, winAmountVal);

        var features = serverResponse.payload.features;
        bool freeGamesTriggered = features != null && features.freeGamesTriggered;
        bool bonusTriggered = features != null && features.bonus != null && features.bonus.triggered;
        int awardedSpins = features != null ? features.awardedSpins : 0;
        string boxId = features != null ? features.boxId : null;

        var result = new SpinResult
        {
            resultMatrix = ConvertReelsToMatrix(serverResponse.payload.reels, gameConfig),
            winAmount = winAmountVal,
            grandTotalWin = winAmountVal,
            winLines = ConvertWinningLines(serverResponse.payload.winningLines, gameConfig),

            playerData = new PlayerData
            {
                balance = newBalance,
                currentBetIndex = 0
            },

            freeSpinData = freeGamesTriggered
                ? new FreeSpinData
                {
                    isTriggered = true,
                    spinsAwarded = awardedSpins,
                    remainingSpins = awardedSpins,
                    isBought = false,
                    boxId = boxId
                }
                : null,

            scatterData = bonusTriggered
                ? new ScatterData
                {
                    isTriggered = true,
                    scatterCount = features.bonus.bonusCount,
                    winAmount = features.bonus.award
                }
                : null,

            overlayScatterData = null,
            stickyWilds = null,

            serverSpinsRemaining = serverResponse.payload.freeSpinsRemaining,
            // Only a *free* spin can end the round. The first base-game spin after a round still
            // reports freeSpinsRemaining: 0, and without the isFreeSpin guard every ordinary spin
            // would look like a round ending.
            isRoundOver = serverResponse.payload.isFreeSpin && serverResponse.payload.freeSpinsRemaining <= 0,
            freeSpinsMultiplier = serverResponse.payload.freeSpinsMultiplier,
            serverFreeSpinTotalWin = features?.freeSpinTotalWin,

            uSpinData = null,
            moneyBagData = null
        };

        return result;
    }

    // Server sends totalResponseRowCount rows per reel (row-major: reels[row][col]). Rows 0 and
    // totalResponseRowCount-1 are decorative rows shown above/below the payline area, but they
    // are still real backend data — all rows are passed through unsliced, in server order.
    // SlotView is responsible for placing each row in the correct on-screen image slot.
    private static List<List<int>> ConvertReelsToMatrix(List<List<string>> serverReels, GameConfig gameConfig)
    {
        int reelCount = gameConfig != null ? gameConfig.reelCount : 3;
        int totalResponseRowCount = gameConfig != null ? gameConfig.totalResponseRowCount : 5;

        if (serverReels == null || serverReels.Count == 0)
        {
            UnityEngine.Debug.LogError("Invalid server reels: serverReels is null or empty");
            return GenerateDefaultMatrix(reelCount, totalResponseRowCount);
        }

        var matrix = new List<List<int>>();

        for (int col = 0; col < reelCount; col++)
        {
            var column = new List<int>();
            for (int serverRow = 0; serverRow < totalResponseRowCount; serverRow++)
            {
                if (serverRow >= serverReels.Count || col >= serverReels[serverRow].Count)
                {
                    UnityEngine.Debug.LogError($"Invalid server data at row {serverRow}, col {col}");
                    column.Add(0);
                    continue;
                }

                string symbolStr = serverReels[serverRow][col];
                if (!int.TryParse(symbolStr, out int symbolId))
                {
                    UnityEngine.Debug.LogError($"Failed to parse symbol: {symbolStr}");
                    column.Add(0);
                    continue;
                }

                column.Add(symbolId);
            }
            matrix.Add(column);
        }

        return matrix;
    }

    private static List<List<int>> GenerateDefaultMatrix(int reelCount, int rowCount)
    {
        var matrix = new List<List<int>>();
        for (int col = 0; col < reelCount; col++)
        {
            var column = new List<int>();
            for (int row = 0; row < rowCount; row++)
            {
                column.Add(0);
            }
            matrix.Add(column);
        }
        return matrix;
    }

    // Converts fixed-payline win data. winningLines[].positions are [row, col] pairs in the
    // server's padded 0..totalResponseRowCount-1 space — offset back to the same local active-row
    // space ConvertReelsToMatrix uses before flattening.
    private static List<WinLine> ConvertWinningLines(List<ServerWinningLine> serverWinningLines, GameConfig gameConfig)
    {
        var winLines = new List<WinLine>();
        if (serverWinningLines == null) return winLines;

        int reelCount = gameConfig != null ? gameConfig.reelCount : 3;
        int rowCount = gameConfig != null ? gameConfig.rowCount : 3;
        int totalResponseRowCount = gameConfig != null ? gameConfig.totalResponseRowCount : 5;
        int activeRowStart = (totalResponseRowCount - rowCount) / 2;

        foreach (var winningLine in serverWinningLines)
        {
            var flatPositions = new List<int>();
            if (winningLine.positions != null)
            {
                foreach (var pos in winningLine.positions)
                {
                    if (pos == null || pos.Count < 2) continue;
                    int serverRow = pos[0];
                    int col = pos[1];
                    int localRow = serverRow - activeRowStart;
                    int flatIndex = localRow * reelCount + col;
                    flatPositions.Add(flatIndex);
                }
            }

            winLines.Add(new WinLine
            {
                lineId = winningLine.lineIndex,
                symbolId = -1, // Not provided directly by fixed-line wins — positions/lineId identify the win
                positions = flatPositions,
                winAmount = winningLine.payout
            });
        }

        return winLines;
    }

    private static double CalculateNewBalance(double currentBalance, double totalPay, double winAmount)
    {
        // currentBalance already reflects StartSpin()'s optimistic upfront deduction of totalPay —
        // don't subtract it again here, only add back the win.
        return currentBalance + winAmount;
    }
}

#endregion

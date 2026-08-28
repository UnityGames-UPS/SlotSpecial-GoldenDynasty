using System;
using System.Collections.Generic;

#region Server Communication Models — Init

[Serializable]
public class InitData
{
    public string id = "initData";
    public ServerGameData gameData;
    public ServerFeatures features;
    public ServerUIData uiData;
    public ServerPlayer player;
    // Golden Dynasty sends no jackpot block. Kept because the platform's separate "jackpot:sync"
    // event reuses these types, and the init-time read is already null-guarded.
    public JackpotData jackpotData;
}

[Serializable]
public class JackpotData
{
    public JackpotValues values;
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
    // The per-line-bet multiplier: total bet = bet * totalLines. Golden Dynasty has no selectable
    // lines, so all of them are always in play and there is no separate "activeLine" any more.
    public int totalLines;
}

[Serializable]
public class ServerFeatures
{
    public ServerFreeGamesFeature freeGames;
    public ServerHoldAndSpinFeature holdAndSpin;
}

[Serializable]
public class ServerFreeGamesFeature
{
    public string description;
    public int triggerCount;      // scatters needed to trigger
    public int awardedCount;      // spins granted on trigger
    public bool retriggerEnabled;
    public int retriggerCount;    // spins granted on retrigger
    public bool mysterySymbol;
}

[Serializable]
public class ServerHoldAndSpinFeature
{
    public string description;
    public int triggerCount;      // orbs needed to trigger
    public int freeSpinsAwarded;  // respins granted, and reset to on each new orb
    public List<double> orbPrizes;
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
    public string name;         // stable identifier ("Wild", "Scatter", "Orb", "Mystery", "A", ...)
    public string displayName;  // player-facing ("Ace", "King", ...)
    // Line paytable, descending from a full-reel match: index 0 = 5-of-a-kind, 1 = 4, 2 = 3, and a
    // 4th entry (Warriors only) = 2. Absent entirely on Orb and Mystery, empty on Wild.
    public List<double> multiplier;
    // Scatter only, and paid on total bet rather than per line — hence its own field.
    public List<double> scatterMultiplier;
    public bool isSpecialSymbol; // true for Wild/Scatter/Orb/Mystery, false for paying symbols
    public bool isBonusSymbol;   // currently false on every symbol; role unclear
    // NOT sent yet, though the backend's own config has them: "type", "description", "minMatch".
    // See TODO.md "Pending backend" — until "type" arrives the four specials are told apart by name.
}

[Serializable]
public class ServerPlayer
{
    public double balance;
}

#endregion

#region Server Communication Models — Spin Result

[Serializable]
public class ServerSpinResponse
{
    public string id = "ResultData";
    public bool success;
    // Row-major and top-level, NOT nested under payload: 3 rows x 5 columns, values as strings.
    // Every row is live — this game has none of the decorative padding rows Sizzling 7s sent.
    public List<List<string>> matrix;
    public ServerPayload payload;
    public ServerResultFeatures features;
    public ServerPlayerBalance player;

    // Per-cell notes, keyed "row:col" in the same row-major space as matrix. Only Mystery uses it
    // today: a cell that landed as a Mystery gets an entry, and matrix already holds the symbol it
    // revealed into. Values are the string "mystery_" + symbol NAME ("mystery_J", "mystery_10",
    // "mystery_Mystery") and are deliberately never parsed — see ConvertMysteryPositions.
    public Dictionary<string, string> cellMetadata;

    // Still deliberately unbound until their populated shape is known: "payload.orbPrizeMap"
    // (always {}) and "features.holdAndSpin.heldPositions" (always []). Newtonsoft ignores
    // undeclared fields, so omitting them costs nothing, whereas guessing a type wrong throws and
    // loses the whole spin.
}

[Serializable]
public class ServerPlayerBalance
{
    // Nullable because the old backend sometimes sent null here. Golden Dynasty has always sent a
    // real number so far, but the guard is free.
    public double? balance;
}

[Serializable]
public class ServerPayload
{
    // The spin's total win, and the only figure to display. The server sends the grand total here
    // and the breakdown separately, so the client never sums anything itself. On a spin with no
    // scatter pay it happens to equal the sum of lineWins[].win, which is all the samples so far
    // have shown — but it is the total, not the line subtotal.
    public double currentWinning;
    // Per-line breakdown, used only for the amount label on each line during the Phase 2 cycle.
    public List<ServerLineWin> lineWins;
    // The scatter pay, on total bet rather than per line. Informational: it is already part of
    // currentWinning. Never add it — that would double-count the scatter on every spin one pays.
    public double scatterWin;
    public int scatterCount;
}

[Serializable]
public class ServerLineWin
{
    public int lineIndex;
    // Reel indices that took part in the win — NOT [row, col] pairs. The row for each reel comes
    // from the payline definition at gameData.lines[lineIndex].
    public List<int> positions;
    public double win;
}

[Serializable]
public class ServerResultFeatures
{
    public ServerFreeGameResult freeGame;
    public ServerHoldAndSpinResult holdAndSpin;
}

[Serializable]
public class ServerFreeGameResult
{
    public bool isFreeGame;
    public int freeGameCount;
    public bool freeGameAdded;
    public string gameType;
    public int currentGameIndex;
    public double totalRoundWin;
}

[Serializable]
public class ServerHoldAndSpinResult
{
    public bool active;
    public bool triggered;
    public int spinsRemaining;
    public int orbCount;
    public int newOrbCount;
    public double totalOrbPayout;
}

#endregion

#region Client-Side Spin Request

[Serializable]
public class SpinRequest
{
    public string type = "SPIN";
    public SpinPayload payload;
}

[Serializable]
public class SpinPayload
{
    // betIndex is the only field with a confirmed effect. The server owns free-spin state, so
    // there is no isFreeSpin flag to send any more.
    public int betIndex;
}

#endregion

#region Game Configuration (Client Side Converted)

[Serializable]
public class GameConfig
{
    public int reelCount = 5;
    // Every row the server sends is live and pays. Sizzling 7s also sent decorative padding rows,
    // which is why a separate totalResponseRowCount and an active-row offset used to exist.
    public int rowCount = 3;

    // Number of paylines, and equally the per-line-bet multiplier: total bet = bet * activeLine.
    public int activeLine = 50;

    public List<List<int>> paylines;
    public List<double> availableBets;
    public List<SymbolInfo> symbols;

    // Resolved from the init symbol table. -1 means "not present", so an unresolved role can never
    // collide with a real symbol id the way a 0 default would.
    public int wildSymbolId = -1;
    public int scatterSymbolId = -1;
    public int orbSymbolId = -1;
    public int mysterySymbolId = -1;
}

[Serializable]
public class SymbolInfo
{
    public int id;
    public string name;
    public string displayName;
    // Descending from a full-reel match: index 0 = reelCount-of-a-kind, 1 = one fewer, and so on.
    public List<double> multipliers;
    // Scatter only; paid on total bet rather than per line.
    public List<double> scatterMultipliers;

    public bool isWild;
    public bool isScatter;
    public bool isOrb;
    public bool isMystery;
    // Straight from the server's isSpecialSymbol — true for all four of the above.
    public bool isSpecial;

    // Fewest matching symbols that pay, derived from the paytable's length. 0 when the symbol has
    // no line paytable at all (Wild, Orb, Mystery).
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
    // Column-major: [reel][row], the transpose of the server's row-major matrix.
    public List<List<int>> resultMatrix;
    public double winAmount;
    public double grandTotalWin;
    public List<WinLine> winLines;
    public PlayerData playerData;

    // Always present — the server sends the free-game block on every spin, in or out of a round.
    public FreeGameData freeGame;

    // Cells that landed as a Mystery symbol, as flat indices (row * reelCount + col) — the same
    // space WinLine.positions uses. resultMatrix already holds what each one revealed into, so
    // these are positions only: draw a Mystery there, play the reveal, uncover what is beneath.
    public List<int> mysteryPositions;

    // Dormant CNY-era holdovers — never populated. Queued for removal in TODO.md "Cleanup".
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
    // Not resolvable from the wire data: the server reports which cells a line covers, not which
    // symbol paid, and wild substitution means the first cell is not reliably the paying symbol.
    public int symbolId;
    // Flat indices into the active grid: row * reelCount + col.
    public List<int> positions;
    public double winAmount;
}

/// <summary>
/// The free-games facts for one spin, straight from features.freeGame. Everything here is
/// server-authoritative per spin; the round-level running totals GameManager needs (spins used,
/// total awarded) are derived from these rather than sent.
/// </summary>
[Serializable]
public class FreeGameData
{
    // True when this spin was itself played on free-game credit. False on the spin that triggers
    // a round — that one is a paid base spin and pays out normally.
    public bool isFreeGame;

    // Spins left AFTER this one. Already decremented by the server, and retriggers are folded in,
    // so it can go up as well as down (6 -> 11 -> 10 -> 15 in a captured round).
    public int spinsRemaining;

    // Set on any spin that awards spins — both the initial trigger and every retrigger. Paired
    // with isFreeGame it distinguishes the two: trigger is (awarded && !isFreeGame), retrigger is
    // (awarded && isFreeGame).
    public bool spinsAwarded;

    // The round's running total, server-authoritative. The old backend sent no aggregate and the
    // client had to accumulate, which drifted whenever a response was missed.
    public double roundWin;
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
    BigWin
}

#endregion

#region Helper Classes for Conversion

/// <summary>
/// The single seam between server JSON and client types. Wire shapes change here and nowhere else,
/// so the view and controller layers never see a backend revision.
/// </summary>
public static class InitDataConverter
{
    internal static GameConfig ConvertToGameConfig(InitData serverData)
    {
        var gameData = serverData?.gameData;
        var serverSymbols = serverData?.uiData?.paylines?.symbols;

        int reelCount = (gameData?.lines != null && gameData.lines.Count > 0 && gameData.lines[0] != null)
            ? gameData.lines[0].Count
            : 5;

        int lineCount = gameData?.lines?.Count ?? 0;
        int totalLines = (gameData != null && gameData.totalLines > 0) ? gameData.totalLines : lineCount;

        var config = new GameConfig
        {
            reelCount = reelCount,
            rowCount = 3,
            activeLine = totalLines,
            paylines = gameData?.lines,
            availableBets = gameData?.bets,
            symbols = new List<SymbolInfo>()
        };

        if (serverSymbols == null)
        {
            UnityEngine.Debug.LogError("[InitDataConverter] Init carried no symbol table — symbol art and paytables will be unresolved.");
            return config;
        }

        foreach (var serverSymbol in serverSymbols)
        {
            if (serverSymbol == null) continue;

            var symbolInfo = new SymbolInfo
            {
                id = serverSymbol.id,
                name = serverSymbol.name,
                displayName = string.IsNullOrEmpty(serverSymbol.displayName) ? serverSymbol.name : serverSymbol.displayName,
                multipliers = serverSymbol.multiplier ?? new List<double>(),
                scatterMultipliers = serverSymbol.scatterMultiplier ?? new List<double>(),
                isSpecial = serverSymbol.isSpecialSymbol
            };

            // -- STOPGAP ---------------------------------------------------------------------
            // The init flags a symbol as special but never says which kind, so the four roles are
            // told apart by matching the server's stable "name". This breaks silently if a symbol
            // is renamed, reordered or localized. Swap this block for serverSymbol.type once the
            // backend sends it — those values already exist in Assets/Scripts/Config/gdn_config.json.
            // Tracked in TODO.md under "Pending backend".
            switch ((serverSymbol.name ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "wild": symbolInfo.isWild = true; break;
                case "scatter": symbolInfo.isScatter = true; break;
                case "orb": symbolInfo.isOrb = true; break;
                case "mystery": symbolInfo.isMystery = true; break;
            }
            // --------------------------------------------------------------------------------

            // A symbol the server calls special that we failed to place is worth shouting about:
            // it means a rename has already happened and the stopgap above has gone stale.
            if (symbolInfo.isSpecial && !symbolInfo.isWild && !symbolInfo.isScatter && !symbolInfo.isOrb && !symbolInfo.isMystery)
            {
                UnityEngine.Debug.LogError($"[InitDataConverter] Symbol id {symbolInfo.id} (name '{symbolInfo.name}') is flagged special but matches no known role. The name-matching stopgap needs updating.");
            }

            symbolInfo.minMatch = DeriveMinMatch(symbolInfo.multipliers.Count, reelCount);

            config.symbols.Add(symbolInfo);

            if (symbolInfo.isWild) config.wildSymbolId = symbolInfo.id;
            if (symbolInfo.isScatter) config.scatterSymbolId = symbolInfo.id;
            if (symbolInfo.isOrb) config.orbSymbolId = symbolInfo.id;
            if (symbolInfo.isMystery) config.mysterySymbolId = symbolInfo.id;
        }

        return config;
    }

    // The paytable runs descending from a full-reel match, so its length says how far down the
    // match counts go: reelCount - (tierCount - 1). Verified against a live spin — Ace's
    // [75, 20, 5] paid 20 for four-of-a-kind, so index 1 is 4-of-a-kind and the table bottoms out
    // at 3. Warriors' 4-entry table is the only one reaching 2.
    private static int DeriveMinMatch(int tierCount, int reelCount)
    {
        if (tierCount <= 0) return 0;
        return Math.Max(1, reelCount - tierCount + 1);
    }

    internal static PlayerData ConvertToPlayerData(ServerPlayer serverPlayer, int defaultBetIndex = 0)
    {
        return new PlayerData
        {
            balance = serverPlayer != null ? serverPlayer.balance : 0,
            currentBetIndex = defaultBetIndex
        };
    }

    /// <summary>
    /// Converts one spin response into the client's SpinResult.
    /// betAmount is accepted for signature stability but no longer used: the server always sends
    /// the post-spin balance, and the fallback below works off the already-deducted local balance.
    /// </summary>
    internal static SpinResult ConvertServerResponseToSpinResult(ServerSpinResponse serverResponse, double currentBalance, double betAmount, GameConfig gameConfig)
    {
        var payload = serverResponse?.payload;

        // currentWinning is the server's grand total for the spin and passes straight through —
        // scatterWin and any other component is already inside it, so nothing is summed here.
        // Verified against a live spin: balance moved by exactly (win - totalBet).
        double winAmountVal = payload?.currentWinning ?? 0;
        double newBalance = serverResponse?.player?.balance ?? CalculateNewBalance(currentBalance, winAmountVal);

        return new SpinResult
        {
            resultMatrix = ConvertMatrixToColumns(serverResponse?.matrix, gameConfig),
            winAmount = winAmountVal,
            grandTotalWin = winAmountVal,
            winLines = ConvertLineWins(payload?.lineWins, gameConfig),

            playerData = new PlayerData
            {
                balance = newBalance,
                currentBetIndex = 0
            },

            freeGame = ConvertFreeGame(serverResponse?.features?.freeGame),
            mysteryPositions = ConvertMysteryPositions(serverResponse?.cellMetadata, gameConfig),

            uSpinData = null,
            moneyBagData = null
        };
    }

    // Never returns null, so the controller can read it without guarding every access. A missing
    // block is indistinguishable from "not in a round", which is the correct reading either way.
    private static FreeGameData ConvertFreeGame(ServerFreeGameResult serverFreeGame)
    {
        if (serverFreeGame == null) return new FreeGameData();

        return new FreeGameData
        {
            isFreeGame = serverFreeGame.isFreeGame,
            spinsRemaining = serverFreeGame.freeGameCount,
            spinsAwarded = serverFreeGame.freeGameAdded,
            roundWin = serverFreeGame.totalRoundWin
        };
    }

    /// <summary>
    /// Turns cellMetadata into the flat cell indices that landed as a Mystery.
    ///
    /// Only the KEYS are read. The values encode the revealed symbol as "mystery_" + its display
    /// name ("mystery_J", "mystery_10", "mystery_Mystery"), which would mean splitting on an
    /// underscore and matching a name — brittle, and pointless: matrix already carries the revealed
    /// symbol at that same cell. So the view draws a Mystery over a cell that is already correct
    /// underneath, and uncovering it is the reveal.
    ///
    /// Keys are "row:col" in the server's row-major space, confirmed against live data — a key of
    /// "1:4" only lands in range read that way on a 3-row matrix.
    /// </summary>
    private static List<int> ConvertMysteryPositions(Dictionary<string, string> cellMetadata, GameConfig gameConfig)
    {
        var positions = new List<int>();
        if (cellMetadata == null || cellMetadata.Count == 0) return positions;

        int reelCount = gameConfig != null ? gameConfig.reelCount : 5;
        int rowCount = gameConfig != null ? gameConfig.rowCount : 3;

        foreach (var entry in cellMetadata)
        {
            string key = entry.Key;
            if (string.IsNullOrEmpty(key)) continue;

            int separator = key.IndexOf(':');
            if (separator <= 0 || separator >= key.Length - 1)
            {
                UnityEngine.Debug.LogError($"[InitDataConverter] cellMetadata key '{key}' is not in the expected row:col form — cell skipped.");
                continue;
            }

            if (!int.TryParse(key.Substring(0, separator), out int row)
                || !int.TryParse(key.Substring(separator + 1), out int col))
            {
                UnityEngine.Debug.LogError($"[InitDataConverter] Could not read row/col out of cellMetadata key '{key}' — cell skipped.");
                continue;
            }

            if (row < 0 || row >= rowCount || col < 0 || col >= reelCount)
            {
                UnityEngine.Debug.LogError($"[InitDataConverter] cellMetadata key '{key}' is outside the {rowCount}x{reelCount} grid — cell skipped.");
                continue;
            }

            positions.Add(row * reelCount + col);
        }

        return positions;
    }

    // Server matrix is row-major (matrix[row][col]); the client works column-major ([reel][row]),
    // so this transposes. No padding rows to skip — every row the server sends is live.
    private static List<List<int>> ConvertMatrixToColumns(List<List<string>> serverMatrix, GameConfig gameConfig)
    {
        int reelCount = gameConfig != null ? gameConfig.reelCount : 5;
        int rowCount = gameConfig != null ? gameConfig.rowCount : 3;

        if (serverMatrix == null || serverMatrix.Count == 0)
        {
            UnityEngine.Debug.LogError("[InitDataConverter] Spin response carried no matrix.");
            return GenerateDefaultMatrix(reelCount, rowCount);
        }

        var matrix = new List<List<int>>();

        for (int col = 0; col < reelCount; col++)
        {
            var column = new List<int>();
            for (int row = 0; row < rowCount; row++)
            {
                if (row >= serverMatrix.Count || serverMatrix[row] == null || col >= serverMatrix[row].Count)
                {
                    UnityEngine.Debug.LogError($"[InitDataConverter] matrix has no cell at row {row}, col {col}.");
                    column.Add(0);
                    continue;
                }

                if (!int.TryParse(serverMatrix[row][col], out int symbolId))
                {
                    UnityEngine.Debug.LogError($"[InitDataConverter] Could not parse symbol at row {row}, col {col}.");
                    symbolId = 0;
                }

                column.Add(symbolId);
            }
            matrix.Add(column);
        }

        return matrix;
    }

    // The server names the reels a win covers; the row each of those reels landed on lives in the
    // payline definition. Resolving that here rather than in SlotView keeps payline knowledge in
    // the model and hands the view the same flat indices it already consumes.
    private static List<WinLine> ConvertLineWins(List<ServerLineWin> lineWins, GameConfig gameConfig)
    {
        var winLines = new List<WinLine>();
        if (lineWins == null) return winLines;

        int reelCount = gameConfig != null ? gameConfig.reelCount : 5;
        int rowCount = gameConfig != null ? gameConfig.rowCount : 3;
        var paylines = gameConfig?.paylines;

        foreach (var lineWin in lineWins)
        {
            if (lineWin == null) continue;

            List<int> payline = (paylines != null && lineWin.lineIndex >= 0 && lineWin.lineIndex < paylines.Count)
                ? paylines[lineWin.lineIndex]
                : null;

            if (payline == null)
            {
                UnityEngine.Debug.LogError($"[InitDataConverter] Win references payline {lineWin.lineIndex}, outside the lines sent at init — win not shown.");
                continue;
            }

            var flatPositions = new List<int>();
            if (lineWin.positions != null)
            {
                foreach (int reelIndex in lineWin.positions)
                {
                    if (reelIndex < 0 || reelIndex >= reelCount || reelIndex >= payline.Count) continue;

                    int row = payline[reelIndex];
                    if (row < 0 || row >= rowCount) continue;

                    flatPositions.Add(row * reelCount + reelIndex);
                }
            }

            winLines.Add(new WinLine
            {
                lineId = lineWin.lineIndex,
                symbolId = -1,
                positions = flatPositions,
                winAmount = lineWin.win
            });
        }

        return winLines;
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

    // currentBalance already reflects StartSpin()'s optimistic upfront deduction of the total bet,
    // so only the win is added back. Only reached if the server omits a balance.
    private static double CalculateNewBalance(double currentBalance, double winAmount)
    {
        return currentBalance + winAmount;
    }
}

#endregion

using System.Security.Cryptography;
using System.Text;
using FocusApp.Core;
namespace FocusApp.Infrastructure.Persistence;

public sealed partial class SqliteLocalDataStore : IMembershipCache
{
    internal const string MembershipMigration = "CREATE TABLE IF NOT EXISTS membership_cache (id INTEGER PRIMARY KEY CHECK(id=1), protected_state BLOB NOT NULL);";
    private static readonly byte[] MembershipEntropy = Encoding.UTF8.GetBytes("FocusApp.Service.Membership.v1");
    public async Task<string?> LoadMembershipAsync(CancellationToken ct)
    {
        await InitializeAsync(ct); await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT protected_state FROM membership_cache WHERE id=1";
        var blob = await command.ExecuteScalarAsync(ct) as byte[];
        return blob is null ? null : Encoding.UTF8.GetString(ProtectedData.Unprotect(blob, MembershipEntropy, DataProtectionScope.CurrentUser));
    }
    public async Task SaveMembershipAsync(string state, CancellationToken ct)
    {
        var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(state), MembershipEntropy, DataProtectionScope.CurrentUser);
        await InitializeAsync(ct); await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO membership_cache(id,protected_state) VALUES(1,$state) ON CONFLICT(id) DO UPDATE SET protected_state=excluded.protected_state";
        command.Parameters.AddWithValue("$state", blob); await command.ExecuteNonQueryAsync(ct);
    }
}

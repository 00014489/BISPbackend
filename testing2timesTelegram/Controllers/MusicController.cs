using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace testing2timesTelegram.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MusicController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<MusicController> _logger;

        public MusicController(IConfiguration configuration, ILogger<MusicController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetUserMusic(string userId)
        {
            var tableName = $"_{userId}"; // User-specific table name
            _logger.LogInformation("Looking for table: {TableName}", tableName);

            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();
                _logger.LogInformation("Database connection opened successfully");

                // Step 1: Check if the table exists
                var checkTableCmd = new NpgsqlCommand(@"
                    SELECT EXISTS (
                        SELECT 1 FROM information_schema.tables 
                        WHERE table_name = @tableName
                    )", conn);
                checkTableCmd.Parameters.AddWithValue("tableName", tableName.ToLower()); // Convert to lowercase for consistency

                var tableExists = (bool)await checkTableCmd.ExecuteScalarAsync();
                if (!tableExists)
                {
                    _logger.LogWarning("Table {TableName} does not exist.", tableName);
                    return NotFound($"Table {tableName} does not exist.");
                }

                // Step 2: Get all columns in the table (for debugging)
                var getColumnsCmd = new NpgsqlCommand(@"
                    SELECT column_name, data_type 
                    FROM information_schema.columns 
                    WHERE table_name = @tableName", conn);
                getColumnsCmd.Parameters.AddWithValue("tableName", tableName.ToLower()); // Ensure table name is lowercase

                _logger.LogInformation("Executing query: {Query}", getColumnsCmd.CommandText);

                var allColumns = new List<string>();
                await using (var reader = await getColumnsCmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        _logger.LogInformation("Column: {ColumnName}, Type: {DataType}", reader.GetString(0), reader.GetString(1));
                        allColumns.Add(reader.GetString(0)); // Add column name for debugging
                    }
                }

                if (allColumns.Count == 0)
                {
                    _logger.LogWarning("No columns found for table: {TableName}", tableName);
                    return NotFound($"No columns found in table {tableName}.");
                }

                _logger.LogInformation("Columns in table {TableName}: {Columns}", tableName, string.Join(", ", allColumns));

                // Step 3: Get boolean columns
                var boolColumns = allColumns.Where(c => c.ToLower() != "id" && c.ToLower() != "file_name" && c.ToLower() != "file_id").ToList(); // Ignore non-boolean columns
                if (boolColumns.Count == 0)
                {
                    _logger.LogWarning("No boolean columns found for table: {TableName}", tableName);
                    return NotFound("No playlist columns found.");
                }

                _logger.LogInformation("Boolean columns found: {Columns}", string.Join(", ", boolColumns));

                // Step 4: Select file_name, file_id, and boolean columns
                var selectedColumns = string.Join(", ", new[] { "id", "file_name", "file_location", "file_id" }.Concat(boolColumns.Select(col => $"\"{col}\"")));
                var selectCmd = new NpgsqlCommand($"SELECT {selectedColumns} FROM \"{tableName}\"", conn);

                _logger.LogInformation("Executing query: {Query}", selectCmd.CommandText);

                var result = new List<Dictionary<string, object>>();
                await using var selectReader = await selectCmd.ExecuteReaderAsync();
                while (await selectReader.ReadAsync())
                {
                    var row = new Dictionary<string, object>
                    {
                        ["id"] = selectReader["id"],
                        ["file_name"] = selectReader["file_name"],
                        ["file_location"] = selectReader["file_location"],
                        ["file_id"] = selectReader["file_id"]
                    };

                    foreach (var col in boolColumns)
                        row[col] = selectReader[col];
                    //var row = new Dictionary<string, object>
                    //{
                    //    ["file_name"] = selectReader["file_name"],
                    //    ["file_id"] = selectReader["file_id"]
                    //};

                    //foreach (var col in boolColumns)
                    //    row[col] = selectReader[col];

                    result.Add(row);
                }

                _logger.LogInformation("Returning {Count} rows of data", result.Count);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching music data for table {TableName}", tableName);
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }
    }
}

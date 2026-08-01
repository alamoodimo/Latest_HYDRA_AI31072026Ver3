using DynamicDashboardCommon.Models;
using DynamicDashboardCommon.Models.SchemaAnalysis;
using DynamicDasboardWebAPI.Repositories;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DynamicDasboardWebAPI.Services.SchemaAnalysis
{
    public class SampleDataService
    {
        private readonly QueryRepository _queryRepository;
        private readonly IConfiguration _config;

        public SampleDataService(
            QueryRepository queryRepository,
            IConfiguration config)
        {
            _queryRepository = queryRepository;
            _config = config;
        }

        public async Task<List<SampleDataResult>> FetchSampleDataAsync(int databaseId, dynamic sampleRequests)
        {
            var results = new List<SampleDataResult>();
            var rowsPerTable = _config.GetValue<int>("SchemaAnalysis:SampleData:RowsPerTable", 2);
            var maxDistinct = _config.GetValue<int>("SchemaAnalysis:SampleData:MaxDistinctValuesForValueSet", 20);

            try
            {
                foreach (var request in sampleRequests)
                {
                    try
                    {
                        var tableName = request.TableName.ToString();
                        var columnName = request.ColumnName.ToString();

                        // Check if value set
                        var distinctQuery = $@"
                            SELECT DISTINCT TOP {maxDistinct + 1} [{columnName}] 
                            FROM [{tableName}]
                            WHERE [{columnName}] IS NOT NULL 
                                AND [{columnName}] != ''
                            ORDER BY [{columnName}]";

                        var distinctData = await _queryRepository.ExecuteQueryOnDatabaseAsync(distinctQuery, databaseId);

                        if (distinctData.Count <= maxDistinct)
                        {
                            // Value set - get counts
                            var valueSetQuery = $@"
                                SELECT 
                                    [{columnName}] as Value,
                                    COUNT(*) as UsageCount
                                FROM [{tableName}]
                                WHERE [{columnName}] IS NOT NULL 
                                    AND [{columnName}] != ''
                                GROUP BY [{columnName}]
                                ORDER BY COUNT(*) DESC";

                            var valueCounts = await _queryRepository.ExecuteQueryOnDatabaseAsync(valueSetQuery, databaseId);

                            var result = new SampleDataResult
                            {
                                TableName = tableName,
                                ColumnName = columnName,
                                DataType = "ValueSet"
                            };

                            foreach (var row in valueCounts)
                            {
                                var value = row["Value"];
                                var count = Convert.ToInt32(row["UsageCount"]);

                                result.DistinctValues.Add(value);
                                result.ValueCounts[value.ToString()] = count;
                            }

                            results.Add(result);
                        }
                        else
                        {
                            // Free text samples
                            var sampleQuery = $@"
                                SELECT TOP {rowsPerTable} [{columnName}]
                                FROM [{tableName}]
                                WHERE [{columnName}] IS NOT NULL 
                                    AND [{columnName}] != ''
                                ORDER BY NEWID()";

                            var samples = await _queryRepository.ExecuteQueryOnDatabaseAsync(sampleQuery, databaseId);

                            var result = new SampleDataResult
                            {
                                TableName = tableName,
                                ColumnName = columnName,
                                DataType = "FreeText"
                            };

                            foreach (var row in samples)
                            {
                                result.SampleValues.Add(row[columnName]);
                            }

                            results.Add(result);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Skip failed columns
                        Console.WriteLine($"Error fetching sample for {request.TableName}.{request.ColumnName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error in FetchSampleDataAsync: {ex.Message}", ex);
            }

            return results;
        }
    }
}
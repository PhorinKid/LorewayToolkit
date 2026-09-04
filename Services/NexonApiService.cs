using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using MabinogiCraftOptimizer.Models;

namespace MabinogiCraftOptimizer.Services;

/// <summary>
/// NEXON Open API 통신 및 로컬 시세 캐싱(Data/auction-cache.json) 서비스
/// </summary>
public class NexonApiService
{
    private static readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("https://open.api.nexon.com/"),
        Timeout = TimeSpan.FromSeconds(10)
    };

    private const string ApiKeyEnvironmentVariable = "NEXON_API_KEY";
    private readonly string _cacheFilePath;
    private readonly object _fileLock = new();

    // 10분 TTL 캐시 유효기간
    public static readonly TimeSpan AuctionCacheDuration = TimeSpan.FromMinutes(10);

    // HTTP 요청 카운터
    private static int _httpRequestCounter = 0;
    public static int HttpRequestCounter => _httpRequestCounter;
    public static void ResetHttpRequestCounter() => Interlocked.Exchange(ref _httpRequestCounter, 0);

    // 진행 중인 비동기 요청 중복 방지 딕셔너리
    private readonly ConcurrentDictionary<string, Task<AuctionFetchResult>> _inFlightRequests = new(StringComparer.OrdinalIgnoreCase);

    // 로컬 시세 캐시 저장소 [아이템명: 시세엔트리]
    private readonly Dictionary<string, AuctionCacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public NexonApiService()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _cacheFilePath = Path.Combine(baseDir, "Data", "auction-cache.json");
        LoadCacheFromFile();
    }

    /// <summary>
    /// 로컬 파일(auction-cache.json)에서 시세 캐시 로드
    /// </summary>
    public void LoadCacheFromFile()
    {
        lock (_fileLock)
        {
            try
            {
                if (!File.Exists(_cacheFilePath)) return;

                string json = File.ReadAllText(_cacheFilePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var loaded = JsonSerializer.Deserialize<Dictionary<string, AuctionCacheEntry>>(json, options);

                if (loaded != null)
                {
                    foreach (var kvp in loaded)
                    {
                        _cache[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[경고] auction-cache.json 로드 실패: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 캐시 데이터를 로컬 파일(auction-cache.json)에 저장
    /// </summary>
    public void SaveCacheToFile()
    {
        lock (_fileLock)
        {
            try
            {
                string? dir = Path.GetDirectoryName(_cacheFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_cache, options);
                File.WriteAllText(_cacheFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[오류] auction-cache.json 저장 실패: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 수동 입력 단가 캐시 저장
    /// </summary>
    public void SaveManualPrice(string itemName, long unitPrice)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return;

        var entry = new AuctionCacheEntry
        {
            ItemName = itemName,
            LowestUnitPrice = unitPrice,
            TotalItemCount = 1,
            RawItems = new List<AuctionItem>
            {
                new() { ItemName = itemName, ItemCount = 1, AuctionPricePerUnit = unitPrice }
            },
            LastUpdated = DateTime.Now
        };

        _cache[itemName] = entry;
        SaveCacheToFile();
    }

    /// <summary>
    /// 수동 입력 단가 캐시 삭제
    /// </summary>
    public void RemoveManualPrice(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return;

        if (_cache.Remove(itemName))
        {
            SaveCacheToFile();
        }
    }

    /// <summary>
    /// 전체 캐시 시세 딕셔너리 반환
    /// </summary>
    public Dictionary<string, long> GetAllCachedPrices()
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _cache)
        {
            result[kvp.Key] = kvp.Value.LowestUnitPrice;
        }
        return result;
    }

    /// <summary>
    /// API Key 조회 (.env -> 환경변수)
    /// </summary>
    public string? GetApiKey()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] searchPaths = new[]
        {
            Path.Combine(baseDir, ".env"),
            Path.Combine(baseDir, "..", "..", "..", ".env"),
            Path.Combine(Directory.GetCurrentDirectory(), ".env"),
            ".env"
        };

        foreach (var path in searchPaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

                        var parts = trimmed.Split('=', 2);
                        if (parts.Length == 2 && string.Equals(parts[0].Trim(), ApiKeyEnvironmentVariable, StringComparison.OrdinalIgnoreCase))
                        {
                            string keyVal = parts[1].Trim().Trim('"', '\'');
                            if (!string.IsNullOrWhiteSpace(keyVal))
                            {
                                return keyVal;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[경고] .env 파일 읽기 실패 ({path}): {ex.Message}");
            }
        }

        return Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
    }

    public AuctionCacheEntry? GetCachedEntry(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return null;
        return _cache.TryGetValue(itemName, out var entry) ? entry : null;
    }

    /// <summary>
    /// 경매장 매물 조회 (10분 TTL 캐시 및 중복 요청 방지)
    /// </summary>
    public async Task<AuctionFetchResult> GetAuctionDataAsync(
        string itemName, 
        bool forceRefresh = false)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return new AuctionFetchResult
            {
                Success = false,
                Items = new List<AuctionItem>(),
                Message = "조회할 아이템 이름이 비어 있습니다.",
                LastUpdated = null,
                FromCache = false,
                IsExpired = false
            };
        }

        string cleanName = itemName.Trim();

        // 1. 메모리 캐시 및 TTL 확인
        bool hasCache = _cache.TryGetValue(cleanName, out var cachedEntry);
        bool isExpired = hasCache && cachedEntry != null && (DateTime.Now - cachedEntry.LastUpdated >= AuctionCacheDuration);
        bool isCacheValid = hasCache && cachedEntry != null && !isExpired;

        // 일반 갱신 시 유효 캐시 재사용
        if (!forceRefresh && isCacheValid && cachedEntry != null)
        {
            return new AuctionFetchResult
            {
                Success = true,
                Items = cachedEntry.RawItems,
                Message = $"[캐시 유지] {cleanName} ({cachedEntry.LastUpdated:HH:mm:ss} 기준)",
                LastUpdated = cachedEntry.LastUpdated,
                FromCache = true,
                IsExpired = false
            };
        }

        // 2. 동시 동일 요청 공유
        return await _inFlightRequests.GetOrAdd(cleanName, (key) => ExecuteHttpRequestWithTrackingAsync(key, cachedEntry, isExpired));
    }

    private async Task<AuctionFetchResult> ExecuteHttpRequestWithTrackingAsync(string itemName, AuctionCacheEntry? cachedEntry, bool isExpired)
    {
        try
        {
            return await ExecuteHttpRequestAsync(itemName, cachedEntry, isExpired);
        }
        finally
        {
            _inFlightRequests.TryRemove(itemName, out _);
        }
    }

    private async Task<AuctionFetchResult> ExecuteHttpRequestAsync(string itemName, AuctionCacheEntry? cachedEntry, bool isExpired)
    {
        // 1. API Key 존재 여부 검증
        string? apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (cachedEntry != null)
            {
                return new AuctionFetchResult
                {
                    Success = false,
                    Items = cachedEntry.RawItems,
                    Message = "API Key 없음 (이전 캐시 시세 유지됨)",
                    LastUpdated = cachedEntry.LastUpdated,
                    FromCache = true,
                    IsExpired = isExpired
                };
            }
            return new AuctionFetchResult
            {
                Success = false,
                Items = new List<AuctionItem>(),
                Message = "API Key가 설정되지 않았습니다. .env 파일 또는 환경변수를 확인해주세요.",
                LastUpdated = null,
                FromCache = false,
                IsExpired = isExpired
            };
        }

        const int maxRetries = 3;
        string lastErrorMessage = "알 수 없는 오류";

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                string encodedItemName = HttpUtility.UrlEncode(itemName.Trim());
                string requestUri = $"mabinogi/v1/auction/list?item_name={encodedItemName}";

                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.Add("x-nxopen-api-key", apiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // NEXON 서버 HTTP 요청
                int reqNum = Interlocked.Increment(ref _httpRequestCounter);
                string logMessage = $"[NEXON API] REQUEST #{reqNum} : {itemName}";
                System.Diagnostics.Debug.WriteLine(logMessage);
                Console.WriteLine(logMessage);

                using var response = await _httpClient.SendAsync(request);
                int statusCode = (int)response.StatusCode;

                // 429 Rate Limit 지수 백오프 재시도
                if (statusCode == 429)
                {
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(400 * attempt);
                        continue;
                    }
                }

                // 5xx 서버 오류 재시도
                if (statusCode >= 500 && attempt < maxRetries)
                {
                    await Task.Delay(300 * attempt);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    lastErrorMessage = statusCode switch
                    {
                        400 => "잘못된 요청입니다. (400)",
                        401 => "유효하지 않은 API Key입니다. (401)",
                        403 => "API 접근 권한이 없습니다. (403)",
                        429 => "API 호출 한도(Rate Limit) 초과 (429)",
                        500 => "NEXON 서버 내부 오류 (500)",
                        _ => $"HTTP 오류: {response.StatusCode} ({statusCode})"
                    };

                    return HandleApiFailure(itemName, lastErrorMessage, cachedEntry, isExpired);
                }

                // 응답 JSON 파싱
                string json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var auctionResponse = JsonSerializer.Deserialize<AuctionResponse>(json, options);

                if (auctionResponse == null || auctionResponse.AuctionItems == null || auctionResponse.AuctionItems.Count == 0)
                {
                    // 매물이 없는 경우 (정상 조회 완료)
                    return new AuctionFetchResult
                    {
                        Success = true,
                        Items = new List<AuctionItem>(),
                        Message = "현재 경매장에 등록된 매물이 없습니다.",
                        LastUpdated = DateTime.Now,
                        FromCache = false,
                        IsExpired = false
                    };
                }

                // 성공 시 메모리 및 로컬 파일 캐시 갱신
                long minPrice = auctionResponse.AuctionItems.Min(x => x.AuctionPricePerUnit);
                var newEntry = new AuctionCacheEntry
                {
                    ItemName = itemName,
                    LowestUnitPrice = minPrice,
                    TotalItemCount = auctionResponse.AuctionItems.Count,
                    RawItems = auctionResponse.AuctionItems,
                    LastUpdated = DateTime.Now
                };

                _cache[itemName] = newEntry;
                SaveCacheToFile(); // 로컬 파일에 영구 저장

                return new AuctionFetchResult
                {
                    Success = true,
                    Items = auctionResponse.AuctionItems,
                    Message = $"성공: {auctionResponse.AuctionItems.Count}개의 매물을 조회했습니다.",
                    LastUpdated = newEntry.LastUpdated,
                    FromCache = false,
                    IsExpired = false
                };
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                // 일시적 소켓/네트워크 타임아웃 시 재시도
                lastErrorMessage = $"네트워크 오류: {ex.Message}";
                await Task.Delay(300 * attempt);
            }
            catch (Exception ex)
            {
                lastErrorMessage = $"네트워크 오류: {ex.Message}";
                return HandleApiFailure(itemName, lastErrorMessage, cachedEntry, isExpired);
            }
        }

        return HandleApiFailure(itemName, lastErrorMessage, cachedEntry, isExpired);
    }

    /// <summary>
    /// API 호출 실패 시 기존 캐시 보존 및 안전한 결과 생성 도우미 (0원으로 덮어쓰지 않음)
    /// </summary>
    private AuctionFetchResult HandleApiFailure(string itemName, string errorReason, AuctionCacheEntry? oldEntry, bool isExpired)
    {
        if (oldEntry != null)
        {
            return new AuctionFetchResult
            {
                Success = false,
                Items = oldEntry.RawItems,
                Message = $"새로고침 실패 ({errorReason}) - 이전 시세({oldEntry.LowestUnitPrice:N0} G)가 유지됩니다.",
                LastUpdated = oldEntry.LastUpdated,
                FromCache = true,
                IsExpired = isExpired
            };
        }

        return new AuctionFetchResult
        {
            Success = false,
            Items = new List<AuctionItem>(),
            Message = errorReason,
            LastUpdated = null,
            FromCache = false,
            IsExpired = isExpired
        };
    }
}

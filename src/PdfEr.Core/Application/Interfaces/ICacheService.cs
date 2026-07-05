namespace PdfEr.Core.Application.Interfaces;

public interface ICacheService
{
    string GetCachePath(string key);
    bool TryGetValue<T>(string key, out T? value) where T : class;
    void SetValue<T>(string key, T value, TimeSpan? expiry = null) where T : class;
    bool Remove(string key);
    void ClearExpired();
}

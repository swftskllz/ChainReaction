using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;
using JsonFormatting = Newtonsoft.Json.Formatting;
using Newtonsoft.Json;
public static class PlayFabLoginService
{
    public static void Configure(string titleId)
    {
        if (string.IsNullOrEmpty(PlayFabSettings.staticSettings.TitleId))
            PlayFabSettings.staticSettings.TitleId = titleId;
    }

    private const string CustomIdKey = "PF_CustomId_v1";

    // Ensure we have a stable local ID for anonymous login
    private static string EnsureCustomId()
    {
        var id = PlayerPrefs.GetString(CustomIdKey, string.Empty);
        if (string.IsNullOrEmpty(id))
        {
            id = System.Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(CustomIdKey, id);
            PlayerPrefs.Save();
        }
        return id;
    }

    /// <summary>
    /// Anonymous login via CustomID for Android/WebGL (and works for iOS too).
    /// </summary>
    public static Task<LoginResult> LoginAnonymousAsync(bool createAccount = true, string displayName = null)
        => LoginWithCustomIdInternalAsync(EnsureCustomId(), createAccount, displayName);

    private static Task<LoginResult> LoginWithCustomIdInternalAsync(string customId, bool createAccount, string displayName)
    {
        var tcs = new TaskCompletionSource<LoginResult>();

        var req = new LoginWithCustomIDRequest
        {
            CustomId = customId,
            CreateAccount = createAccount,
            InfoRequestParameters = new GetPlayerCombinedInfoRequestParams
            {
                GetPlayerProfile = true,
                GetUserAccountInfo = true,
                GetUserData = true,
                GetUserReadOnlyData = true
            }
        };

        PlayFabClientAPI.LoginWithCustomID(req, async result =>
        {
            // Optionally set a display name on first login
            if (createAccount && !string.IsNullOrEmpty(displayName))
            {
                try { await TrySetDisplayNameAsync(displayName); } catch { /* swallow */ }
            }

            tcs.SetResult(result);
        },
        error =>
        {
            tcs.SetException(new Exception($"PlayFab Login failed: {error.Error} - {error.ErrorMessage}"));
        });

        return tcs.Task;
    }

    public static Task UpdateContactEmailAsync(string email)
    {
        var tcs = new TaskCompletionSource<bool>();
        PlayFabClientAPI.AddOrUpdateContactEmail(new AddOrUpdateContactEmailRequest { EmailAddress = email },
            _ => tcs.SetResult(true),
            e => tcs.SetException(new Exception(e.GenerateErrorReport())));
        return tcs.Task;
    }

    public static Task TrySetDisplayNameAsync(string displayName)
    {
        var tcs = new TaskCompletionSource<bool>();
        PlayFabClientAPI.UpdateUserTitleDisplayName(new UpdateUserTitleDisplayNameRequest { DisplayName = displayName },
            _ => tcs.SetResult(true),
            e => tcs.SetException(new Exception(e.GenerateErrorReport())));
        return tcs.Task;
    }
    
    public static Task LinkGoogleAsync(string serverAuthCode, bool forceLink = false)
    {
        var tcs = new TaskCompletionSource<bool>();
        var req = new LinkGoogleAccountRequest
        {
            ServerAuthCode = serverAuthCode,
            ForceLink = forceLink
        };
        PlayFabClientAPI.LinkGoogleAccount(req,
            _ => tcs.SetResult(true),
            e => tcs.SetException(new Exception(e.GenerateErrorReport())));
        return tcs.Task;
    }
    
    public static Task LinkAppleAsync(string identityToken, bool forceLink = false)
    {
        var tcs = new TaskCompletionSource<bool>();
        var req = new LinkAppleRequest
        {
            IdentityToken = identityToken,
            ForceLink = forceLink
        };
        PlayFabClientAPI.LinkApple(req,
            _ => tcs.SetResult(true),
            e => tcs.SetException(new Exception(e.GenerateErrorReport())));
        return tcs.Task;
    }

    public static Task LinkEmailPasswordAsync(string email, string password, bool forceLink = false)
    {
        var tcs = new TaskCompletionSource<bool>();
        var req = new AddUsernamePasswordRequest
        {
            Email = email,
            Password = password,
            Username = email 
        };
        PlayFabClientAPI.AddUsernamePassword(req,
            _ => tcs.SetResult(true),
            e =>
            {
                // If already linked, you might want to call LoginWithEmailAddress next
                tcs.SetException(new Exception(e.GenerateErrorReport()));
            });
        return tcs.Task;
    }
    
    public static Task SetUserDataAsync(string key, string value, bool readOnly = false)
    {
        var tcs = new TaskCompletionSource<bool>();
        if (readOnly)
        {
            PlayFabClientAPI.UpdateUserData(new UpdateUserDataRequest
            {
                Permission = UserDataPermission.Public,
                Data = new System.Collections.Generic.Dictionary<string, string> { { key, value } }
            },
            _ => tcs.SetResult(true),
            e => tcs.SetException(new Exception(e.GenerateErrorReport())));
        }
        else
        {
            PlayFabClientAPI.UpdateUserData(new UpdateUserDataRequest
            {
                Data = new System.Collections.Generic.Dictionary<string, string> { { key, value } }
            },
            _ => tcs.SetResult(true),
            e => tcs.SetException(new Exception(e.GenerateErrorReport())));
        }
        return tcs.Task;
    }

    public static Task<string> GetUserDataAsync(string key)
    {
        var tcs = new TaskCompletionSource<string>();
        PlayFabClientAPI.GetUserData(new GetUserDataRequest(),
            res =>
            {
                if (res.Data != null && res.Data.TryGetValue(key, out var kv))
                    tcs.SetResult(kv.Value);
                else
                    tcs.SetResult(null);
            },
            e => tcs.SetException(new Exception(e.GenerateErrorReport())));
        return tcs.Task;
    }
    
    /// <summary>
    /// Save a serializable object as JSON under a given key in PlayFab UserData.
    /// </summary>
    public static Task SaveJsonAsync<T>(string key, T obj, bool readOnly = false)
    {
        var json = JsonConvert.SerializeObject(obj, Formatting.None);
        var tcs = new TaskCompletionSource<bool>();

        var req = new UpdateUserDataRequest
        {
            Data = new Dictionary<string, string> { { key, json } },
            Permission = readOnly ? UserDataPermission.Public : UserDataPermission.Private
        };

        PlayFabClientAPI.UpdateUserData(req,
            _ => tcs.SetResult(true),
            e => tcs.SetException(new Exception(e.GenerateErrorReport())));

        return tcs.Task;
    }

    /// <summary>
    /// Load JSON from a given key and deserialize into type T. Returns default(T) if not found.
    /// </summary>
    public static Task<T> LoadJsonAsync<T>(string key)
    {
        var tcs = new TaskCompletionSource<T>();

        PlayFabClientAPI.GetUserData(new GetUserDataRequest(),
            res =>
            {
                if (res.Data != null && res.Data.TryGetValue(key, out var kv))
                {
                    try
                    {
                        var obj = JsonConvert.DeserializeObject<T>(kv.Value);
                        tcs.SetResult(obj);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(new Exception($"JSON parse failed for key {key}: {ex.Message}"));
                    }
                }
                else
                {
                    tcs.SetResult(default); // no key found
                }
            },
            e => tcs.SetException(new Exception(e.GenerateErrorReport())));

        return tcs.Task;
    }
}
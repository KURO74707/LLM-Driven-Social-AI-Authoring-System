using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Talks to a local LM Studio server (OpenAI-compatible chat API).
/// Attach this to ONE GameObject (e.g. a "GameManagers" object) and reference
/// it from your NPC dialogue scripts.
/// </summary>
public class LLMClient : MonoBehaviour
{
    [Header("Server")]
    [Tooltip("LM Studio local server endpoint. Match the port shown in LM Studio's Developer tab.")]
    [SerializeField] private string apiUrl = "http://localhost:1235/v1/chat/completions";
    [Tooltip("Optional second localhost endpoint for another developer's LM Studio port.")]
    [SerializeField] private string fallbackApiUrl = "http://localhost:1234/v1/chat/completions";
    [Tooltip("Model identifier shown in LM Studio's Developer tab or GET /v1/models.")]
    [SerializeField] private string model = string.Empty;
    [Tooltip("Optional model identifier for the fallback endpoint. Leave empty to reuse Model.")]
    [SerializeField] private string fallbackModel = string.Empty;
    [Tooltip("Ask LM Studio for a JSON object containing the NPC action and dialogue.")]
    [SerializeField] private bool useNpcResponseSchema;

    [Header("Generation settings")]
    [SerializeField] private float temperature = 0.8f;
    [InspectorName("Max Response Tokens")]
    [Tooltip("Maximum generated tokens sent to LM Studio. The UI preserves all received text.")]
    [Min(1)] [SerializeField] private int maxTokens = 200;
    [Tooltip("Request timeout in seconds. If the model is slow, raise this.")]
    [SerializeField] private int timeoutSeconds = 120;

    /// <summary> Fired when a reply arrives. Receives the assistant's text. </summary>
    public event Action<string> OnReply;

    /// <summary> Fired on any network/server error. Receives a human-readable message. </summary>
    public event Action<string> OnError;

    private bool busy;
    private object requestOwner;
    private Action<string> requestFailure;
    private string preferredApiUrl;

    public bool IsBusy => busy;
    public bool LastResponseHitTokenLimit { get; private set; }

    /// <summary>
    /// Sends a user message along with the conversation history.
    /// History should already contain the system prompt and prior turns.
    /// </summary>
    public void SendMessage(string userText, ChatMessage[] history)
    {
        if (string.IsNullOrWhiteSpace(userText)) return;
        SendRequest(userText, history, reply => OnReply?.Invoke(reply), error => OnError?.Invoke(error));
    }

    /// <summary>A scoped request uses the same transport without broadcasting its result to NPC dialogue listeners.</summary>
    public bool SendRequest(string userText, ChatMessage[] history, Action<string> onReply,
        Action<string> onError, JObject responseFormat = null, object owner = null)
    {
        if (string.IsNullOrWhiteSpace(userText))
        {
            onError?.Invoke("The model request is empty.");
            return false;
        }
        if (!isActiveAndEnabled)
        {
            onError?.Invoke("LLMClient is disabled. Enable it before chatting.");
            return false;
        }
        if (busy)
        {
            onError?.Invoke("The model is busy. Please try again shortly.");
            return false;
        }
        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            onError?.Invoke("Set a valid HTTP chat completions URL on LLMClient.");
            return false;
        }
        if (!string.IsNullOrWhiteSpace(fallbackApiUrl) &&
            (!Uri.TryCreate(fallbackApiUrl, UriKind.Absolute, out var fallbackUri) ||
             (fallbackUri.Scheme != Uri.UriSchemeHttp && fallbackUri.Scheme != Uri.UriSchemeHttps)))
        {
            onError?.Invoke("Set a valid optional fallback HTTP URL on LLMClient.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(model))
        {
            onError?.Invoke("Load a chat model in LM Studio and set its identifier on LLMClient.");
            return false;
        }
        string json;
        try { json = BuildRequestJson(userText, history, responseFormat); }
        catch (Exception ex)
        {
            onError?.Invoke("Could not prepare the model request: " + ex.Message);
            return false;
        }
        busy = true;
        requestOwner = owner;
        requestFailure = onError;
        LastResponseHitTokenLimit = false;
        StartCoroutine(PostRoutine(json, onReply, onError));
        return true;
    }

    private string BuildRequestJson(string userText, ChatMessage[] history, JObject responseFormat)
    {
        history = history ?? Array.Empty<ChatMessage>();
        // Full message array = [system prompt + past turns] + new user message
        var all = new ChatMessage[history.Length + 1];
        Array.Copy(history, all, history.Length);
        all[history.Length] = new ChatMessage("user", userText);

        var payload = new Request
        {
            model = model,
            messages = all,
            temperature = temperature,
            max_tokens = Mathf.Max(1, maxTokens),
            stream = false
        };

        string json = JsonUtility.ToJson(payload);
        if (responseFormat != null || useNpcResponseSchema)
        {
            var body = JObject.Parse(json);
            body["response_format"] = responseFormat?.DeepClone() ?? JObject.Parse(@"{
                'type':'json_schema',
                'json_schema':{'name':'npc_response','strict':true,'schema':{
                    'type':'object','properties':{'action':{'type':'string'},'dialogue':{'type':'string'}},
                    'required':['action','dialogue'],'additionalProperties':false}}
            }");
            json = body.ToString(Formatting.None);
        }
        return json;
    }

    private IEnumerator PostRoutine(string json, Action<string> onReply, Action<string> onError)
    {
        string first = string.IsNullOrWhiteSpace(preferredApiUrl) ? apiUrl : preferredApiUrl;
        string second = string.Equals(first, apiUrl, StringComparison.OrdinalIgnoreCase) ? fallbackApiUrl : apiUrl;
        string[] endpoints = { first, second };
        for (int i = 0; i < endpoints.Length; i++)
        {
            string endpoint = endpoints[i];
            if (string.IsNullOrWhiteSpace(endpoint) ||
                (i > 0 && string.Equals(endpoint, first, StringComparison.OrdinalIgnoreCase)))
                continue;
            string endpointJson = json;
            if (string.Equals(endpoint, fallbackApiUrl, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(fallbackModel))
            {
                var body = JObject.Parse(json);
                body["model"] = fallbackModel;
                endpointJson = body.ToString(Formatting.None);
            }
            using var request = new UnityWebRequest(endpoint, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(endpointJson));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = Mathf.Max(1, timeoutSeconds);
            string startError = null;
            UnityWebRequestAsyncOperation operation = null;
            try { operation = request.SendWebRequest(); }
            catch (Exception ex) { startError = ex.Message; }
            if (startError == null) yield return operation;
            bool canTryOther = i == 0 && !string.IsNullOrWhiteSpace(second) &&
                !string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
            bool refused = startError != null ||
                (request.result == UnityWebRequest.Result.ConnectionError &&
                 (request.error ?? "").IndexOf("tim", StringComparison.OrdinalIgnoreCase) < 0);
            string errorBody = request.downloadHandler?.text ?? string.Empty;
            bool missingModel = request.result == UnityWebRequest.Result.ProtocolError &&
                (request.responseCode == 404 || (request.responseCode == 400 &&
                    (errorBody.IndexOf("No models loaded", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     errorBody.IndexOf("model not loaded", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     errorBody.IndexOf("model not found", StringComparison.OrdinalIgnoreCase) >= 0)));
            if (canTryOther && (refused || missingModel))
            {
                preferredApiUrl = null;
                continue;
            }
            // Release before callbacks so the next NPC or Director request can start.
            ReleaseRequest();
            if (startError != null)
            {
                onError?.Invoke("Could not start the LM Studio request: " + startError);
                yield break;
            }
            if (request.result != UnityWebRequest.Result.Success)
            {
                string err = DescribeFailure(request);
                Debug.LogWarning($"LLMClient: {err}", this);
                onError?.Invoke(err);
                yield break;
            }
            preferredApiUrl = endpoint;
            Response response;
            try { response = JsonUtility.FromJson<Response>(request.downloadHandler.text); }
            catch (Exception ex)
            {
                Debug.LogError($"LLMClient: could not parse server response.\n{ex}");
                onError?.Invoke("Bad response from model server.");
                yield break;
            }
            if (response == null || response.choices == null || response.choices.Length == 0)
            {
                onError?.Invoke("LM Studio returned no reply. Load a chat model and try again.");
                yield break;
            }
            Choice choice = response.choices[0];
            LastResponseHitTokenLimit = choice.finish_reason == "length";
            string reply = choice.message?.content;
            if (string.IsNullOrWhiteSpace(reply))
            {
                string usage = response.usage == null ? "unknown" : response.usage.completion_tokens.ToString();
                string detail = $"LM Studio returned no final reply (finish_reason={choice.finish_reason ?? "unknown"}, completion_tokens={usage}).";
                if (LastResponseHitTokenLimit) detail += " Raise Max Response Tokens or shorten the prompt.";
                Debug.LogWarning("LLMClient: " + detail, this);
                onError?.Invoke(detail);
                yield break;
            }
            onReply?.Invoke(reply);
            yield break;
        }
        ReleaseRequest();
        onError?.Invoke("No LM Studio endpoint is configured.");
    }

    /// <summary>Only the owner of the active scoped request can cancel it.</summary>
    public bool CancelRequest(object owner, string reason = "The model request was cancelled.")
    {
        if (!busy || !ReferenceEquals(owner, requestOwner)) return false;
        CancelCurrentRequest(reason);
        return true;
    }

    private void CancelCurrentRequest(string reason)
    {
        var failure = requestFailure;
        StopAllCoroutines();
        ReleaseRequest();
        failure?.Invoke(reason);
    }

    private void ReleaseRequest()
    {
        busy = false;
        requestOwner = null;
        requestFailure = null;
    }

    private void OnDisable()
    {
        if (busy) CancelCurrentRequest("The dialogue request was cancelled because LLMClient was disabled.");
    }

    private static string DescribeFailure(UnityWebRequest request)
    {
        if (request.result == UnityWebRequest.Result.ConnectionError)
        {
            if ((request.error ?? string.Empty).IndexOf("tim", StringComparison.OrdinalIgnoreCase) >= 0)
                return "LM Studio timed out. Try again or increase Timeout Seconds.";
            return "Cannot reach LM Studio. Start its server and check LLMClient's API URL.";
        }
        // Include the server's explanation (e.g. model not loaded / wrong model ID).
        string detail = null;
        try
        {
            var error = JObject.Parse(request.downloadHandler.text)["error"];
            detail = error?.Type == JTokenType.String ? (string)error : (string)error?["message"];
        }
        catch (Exception) { /* Non-JSON error pages still get a useful fallback. */ }
        if (!string.IsNullOrWhiteSpace(detail))
            return $"LM Studio HTTP {request.responseCode}: {detail}";
        return $"LM Studio request failed (HTTP {request.responseCode}). Check that a chat model is loaded. {request.error}";
    }

    // ---- JSON shapes (JsonUtility needs concrete classes; arrays can't be top-level) ----

    [Serializable]
    private class Request
    {
        public string model;
        public ChatMessage[] messages;
        public float temperature;
        public int max_tokens;
        public bool stream;
    }

    [Serializable]
    private class Response
    {
        public Choice[] choices;
        public Usage usage;
    }

    [Serializable]
    private class Usage
    {
        public int completion_tokens;
    }

    [Serializable]
    private class Choice
    {
        public Message message;
        public string finish_reason;
    }

    [Serializable]
    private class Message
    {
        public string role;
        public string content;
    }

    [Serializable]
    private class ErrorResponse
    {
        public ServerError error;
    }

    [Serializable]
    private class ServerError
    {
        public string message;
    }
}

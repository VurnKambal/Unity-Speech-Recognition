using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;

public class SpeechRecognitionManager : MonoBehaviour
{
    private UIDocument uiDocument;
    private DropdownField modelDropdown;
    private DropdownField micDropdown;
    private Button startButton;
    private Button stopButton;
    private TextField textInput;
    private Label outputText;
    private Label differenceLabel; // New label to display the difference
    private Label mistakesLabel; // New label to display mistakes

    private string apiUrl = "http://localhost:5000";
    private bool isRecording = false;
    private Coroutine transcriptionCoroutine;
    private List<string> processedTranscriptions = new List<string>();

    void OnEnable()
    {
        uiDocument = GetComponent<UIDocument>();
        var root = uiDocument.rootVisualElement;

        modelDropdown = root.Q<DropdownField>("ModelDropdown");
        micDropdown = root.Q<DropdownField>("MicDropdown");
        startButton = root.Q<Button>("StartButton");
        stopButton = root.Q<Button>("StopButton");
        textInput = root.Q<TextField>("TextInput");
        outputText = root.Q<VisualElement>("Container").Q<VisualElement>().Q<Label>("Transcription");
        differenceLabel = root.Q<Label>("TranscriptionDiff");
        mistakesLabel = root.Q<Label>("Mistakes");

        modelDropdown.RegisterValueChangedCallback(evt => OnModelSelected(evt.newValue));
        startButton.clicked += StartRecording;
        stopButton.clicked += StopRecording;

        PopulateModelDropdown();
        PopulateMicrophoneDropdown();

        ClearAllText();
    }

    void ClearAllText()
    {
        outputText.text = "";
        differenceLabel.text = "Difference:";
        mistakesLabel.text = "MIstakes: ";
    }

    void PopulateModelDropdown()
    {
        List<string> options = new List<string> { "Normal", "Lightweight" };
        modelDropdown.choices = options;
        modelDropdown.value = options[1]; // Set default to Lightweight
        OnModelSelected(options[1]); // Load the default model
    }

    void PopulateMicrophoneDropdown()
    {
        StartCoroutine(GetAudioDevices());
    }

    void OnModelSelected(string modelName)
    {
        StartCoroutine(SendLoadModelRequest(modelName));
    }

    IEnumerator SendLoadModelRequest(string modelName)
    {
        string jsonPayload = JsonUtility.ToJson(new { model = modelName });
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);

        using (UnityWebRequest www = new UnityWebRequest($"{apiUrl}/load_model", "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"Model {modelName} loaded successfully");
            }
            else
            {
                Debug.LogError($"Error loading model {modelName}: " + www.error);
            }
        }
    }

    IEnumerator GetAudioDevices()
    {
        using (UnityWebRequest www = UnityWebRequest.Get($"{apiUrl}/list_audio_devices"))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                string jsonResult = www.downloadHandler.text;
                AudioDeviceList deviceList = JsonUtility.FromJson<AudioDeviceList>(jsonResult);

                List<string> options = new List<string>();
                foreach (AudioDevice device in deviceList.devices)
                {
                    options.Add(device.name);
                }
                micDropdown.choices = options;
                
                if (options.Count > 1)
                {
                    micDropdown.index = 1;
                }
                else if (options.Count > 0)
                {
                    micDropdown.index = 0;
                }
            }
            else
            {
                Debug.LogError("Error getting audio devices: " + www.error);
            }
        }
    }

    void StartRecording()
    {
        if (!isRecording)
        {
            isRecording = true;
            ClearAllText();
            processedTranscriptions.Clear(); // Clear the list of processed transcriptions
            StartCoroutine(SendRequest("start_recording", "POST"));
            transcriptionCoroutine = StartCoroutine(GetRealtimeTranscription());
        }
    }

    void StopRecording()
    {
        if (isRecording)
        {
            isRecording = false;
            StartCoroutine(SendRequest("stop_recording", "POST"));
            if (transcriptionCoroutine != null)
            {
                StopCoroutine(transcriptionCoroutine);
            }
            // The final transcription is already in the outputText and textInput
            // No need for GetFinalTranscription() unless you want to fetch any last-minute changes
        }
    }

    IEnumerator SendRequest(string endpoint, string method)
    {
        using (UnityWebRequest www = new UnityWebRequest($"{apiUrl}/{endpoint}", method))
        {
            www.downloadHandler = new DownloadHandlerBuffer();

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"{endpoint} request successful");
            }
            else
            {
                Debug.LogError($"Error in {endpoint}: " + www.error);
            }
        }
    }

    IEnumerator GetRealtimeTranscription()
    {
        string finalText = "";
        List<string> processedTranscriptions = new List<string>();

        while (isRecording)
        {
            using (UnityWebRequest www = UnityWebRequest.Get($"{apiUrl}/get_transcription"))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    string jsonResult = www.downloadHandler.text;
                    TranscriptionResult result = JsonUtility.FromJson<TranscriptionResult>(jsonResult);
                    
                    if (result.transcription.Count > 0)
                    {
                        foreach (string text in result.transcription)
                        {
                            if (!processedTranscriptions.Contains(text))
                            {
                                if (!string.IsNullOrEmpty(finalText))
                                {
                                    finalText += " ";
                                }
                                finalText += text;
                                processedTranscriptions.Add(text);
                            }
                        }

                        Debug.Log(finalText);
                        outputText.text = finalText;
                        
                        // Calculate difference
                        StartCoroutine(CalculateDifference(textInput.value, finalText));
                    }
                }
                else
                {
                    Debug.LogError("Error getting transcription: " + www.error);
                }
            }

            yield return new WaitForSeconds(1f); // Poll every 1 second
        }
    }

    IEnumerator CalculateDifference(string text_input, string transcription)
    {
        Debug.Log("Calculating Difference");
        if (differenceLabel == null)
        {
            Debug.LogError("DifferenceLabel is null. Cannot update difference.");
            yield break;
        }


        // Create and populate the payload
        Payload payload = new Payload { text_input = textInput.value };
        string jsonPayload = JsonUtility.ToJson(payload);
        Debug.Log($"Sending JSON payload: {jsonPayload} | Text Input: {textInput.value}");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);

        using (UnityWebRequest www = new UnityWebRequest($"{apiUrl}/get_difference", "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                string jsonResult = www.downloadHandler.text;
                DifferenceResult result = JsonUtility.FromJson<DifferenceResult>(jsonResult);
                
                // Update the difference label
                differenceLabel.text = $"Difference: {result.difference:P2}";
                
                // Display mistakes
                DisplayMistakes(result.mistakes);
                
                Debug.Log($"Updated difference: {result.difference:P2}");
            }
            else
            {
                Debug.LogError($"Error calculating difference: {www.error}");
            }
        }
    }

    void DisplayMistakes(Mistake[] mistakes)
    {
        var missingWords = new List<string>();
        var extraWords = new List<string>();
        var incorrectWords = new List<string>();

        foreach (var mistake in mistakes)
        {
            switch (mistake.type)
            {
                case "removed":
                    missingWords.Add(mistake.word);
                    break;
                case "added":
                    extraWords.Add(mistake.word);
                    break;
                case "changed":
                    incorrectWords.Add(mistake.word);
                    break;
            }
        }

        string mistakeText = "Mistakes:\n";

        if (missingWords.Any())
            mistakeText += $"Missing: {string.Join(", ", missingWords)}\n";

        if (extraWords.Any())
            mistakeText += $"Extra: {string.Join(", ", extraWords)}\n";

        if (incorrectWords.Any())
            mistakeText += $"Incorrect: {string.Join(", ", incorrectWords)}\n";

        if (!missingWords.Any() && !extraWords.Any() && !incorrectWords.Any())
            mistakeText += "No mistakes found.";

        mistakesLabel.text = mistakeText;
    }
}

[System.Serializable]
public class AudioDevice
{
    public string name;
}

[System.Serializable]
public class AudioDeviceList
{
    public List<AudioDevice> devices;
}

[System.Serializable]
public class TranscriptionResult
{
    public List<string> transcription;
}

[System.Serializable]
public class DifferenceResult
{
    public string input;
    public string transcription;
    public float difference;
    public Mistake[] mistakes;
}

[System.Serializable]
public class Mistake
{
    public string type;
    public string word;
    public int index;
}
// Create a serializable class for the payload
[System.Serializable]
public class Payload
{
    public string text_input;
}
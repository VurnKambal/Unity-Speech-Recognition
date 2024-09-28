import difflib
import nltk
from nltk.tokenize import word_tokenize
nltk.download('punkt_tab')  # Download the necessary data for the tokenizer
from flask import Flask, request, jsonify
import pyaudio
import json
from vosk import Model, KaldiRecognizer
from threading import Thread
from queue import Queue
import io
import re


app = Flask(__name__)

CHANNELS = 1
FRAME_RATE = 16000
RECORD_SECONDS = 1
AUDIO_FORMAT = pyaudio.paInt16
SAMPLE_SIZE = 5
INPUT_DEVICE_INDEX = 4

messages = Queue()
recordings = Queue()
current_transcription = []  # Global transcription buffer

models = {
    "Normal": "vosk-model-en-us-0.22",
    "Lightweight": "vosk-model-small-en-us-0.15"
}

current_model = None
rec = None

def load_model(model_name):
    global current_model, rec
    current_model = Model(f"models/{models[model_name]}")
    rec = KaldiRecognizer(current_model, FRAME_RATE)
    rec.SetWords(True)
    print("Loaded Model")

def record_microphone(chunk=1024):
    p = pyaudio.PyAudio()

    stream = p.open(
        format=AUDIO_FORMAT,
        channels=CHANNELS,
        rate=FRAME_RATE,
        input=True,
        input_device_index=INPUT_DEVICE_INDEX,
        frames_per_buffer=chunk
    )
    
    frames = []
    while not messages.empty():
        data = stream.read(chunk)
        frames.append(data)

        if len(frames) >= (FRAME_RATE * RECORD_SECONDS) / chunk:
            recordings.put(frames.copy())
            frames = []

    stream.stop_stream()
    stream.close()
    p.terminate()

def speech_recognition():
    global current_transcription
    
    while not messages.empty():
        if not recordings.empty():
            print("transcribing")
            frames = recordings.get()
            rec.AcceptWaveform(b''.join(frames))
            result = json.loads(rec.Result())
            if result.get("text", ""):
                current_transcription.append(result["text"])
                print(f"Recognized: {result['text']}")

@app.route('/load_model', methods=['POST'])
def load_model_endpoint():
    model_name = request.json.get('model', 'Lightweight')
    if model_name not in models:
        return jsonify({"error": "Invalid model name"}), 400
    
    load_model(model_name)
    return jsonify({"status": f"Model {model_name} loaded successfully"}), 200

@app.route('/start_recording', methods=['POST'])
def start_recording():
    global current_transcription
    
    # Clear the recognizer
    if rec:
        rec.Reset()
    
    print("\n\nStart Recording")
    current_transcription = []
    messages.put(True)
    record = Thread(target=record_microphone)
    record.start()
    
    transcribe = Thread(target=speech_recognition)
    transcribe.start()
    
    return jsonify({"status": "Recording started"}), 200

@app.route('/stop_recording', methods=['POST'])
def stop_recording():
    global current_transcription
    
    while not messages.empty():
        messages.get()
    
    # Clear the recognizer
    if rec:
        rec.Reset()
    
    # Clear the current transcription
    current_transcription = []
    
    return jsonify({"status": "Recording stopped and results cleared"}), 200

@app.route('/get_transcription', methods=['GET'])
def get_transcription():
    return jsonify({"transcription": current_transcription}), 200

@app.route('/list_audio_devices', methods=['GET'])
def list_audio_devices():
    p = pyaudio.PyAudio()
    devices = []
    for i in range(p.get_device_count()):
        device_info = p.get_device_info_by_index(i)
        if device_info.get('maxInputChannels') > 0:
            devices.append(device_info)
    p.terminate()
    return jsonify({"devices": devices}), 200

@app.route('/list_models', methods=['GET'])
def list_models():
    return jsonify({"models": list(models.keys())}), 200

@app.route('/get_difference', methods=['POST'])
def get_difference():
    print("Received request data:", request.data)
    print("Received headers:", request.headers)
    try:
        data = request.get_json(force=True)
        print("Parsed JSON data:", data)
        input_text = data.get('text_input', '')
    except Exception as e:
        print("Error parsing JSON:", str(e))
        input_text = ''
    
    transcription = ' '.join(current_transcription)
    
    print("Input text:", input_text)
    print("Transcription:", transcription)
    
    # Calculate similarity ratio
    similarity = difflib.SequenceMatcher(None, input_text, transcription).ratio()
    
    # Calculate difference
    difference = 1 - similarity
    
    # Compare texts and get mistakes
    mistakes = compare_texts(input_text, transcription)
    
    response = {
        "input": input_text,
        "transcription": transcription,
        "difference": difference,
        "mistakes": mistakes
    }
    print("Sending response:", response)
    return jsonify(response), 200

def compare_texts(actual, transcribed):
    # Remove symbols except numbers and convert to lowercase
    actual_clean = re.sub(r'[^\w\s\d]', '', actual.lower())
    transcribed_clean = re.sub(r'[^\w\s\d]', '', transcribed.lower())

    # Tokenize the cleaned texts
    actual_words = word_tokenize(actual_clean)
    transcribed_words = word_tokenize(transcribed_clean)

    print("\n\nCleaned and tokenized words (including numbers):")
    print(actual_words)
    print(transcribed_words)

    differ = difflib.Differ()
    diff = list(differ.compare(actual_words, transcribed_words))
    
    mistakes = []
    for i, word in enumerate(diff):
        if word.startswith('- '):
            mistakes.append({"type": "removed", "word": word[2:], "index": i})
        elif word.startswith('+ '):
            mistakes.append({"type": "added", "word": word[2:], "index": i})
        elif word.startswith('? '):
            if i > 0 and not diff[i-1].startswith('  '):
                mistakes[-1]["type"] = "changed"
    
    return mistakes

if __name__ == '__main__':
    app.run(debug=True)
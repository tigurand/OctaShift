# 🎵 OctaShift

OctaShift is a simple desktop tool for converting MIDI files into limited key ranges commonly used by in-game musical performance systems.

Designed for games like:

- Final Fantasy XIV (37 Keys)
- Where Winds Meet (36 Keys)
- Heartopia (15 Keys)

It automatically shifts notes into supported ranges while preserving musical structure as much as possible.

---

## ✨ Features

- 🎹 37 Keys Mode (FFXIV compatible)
- 🎼 36 Keys Mode (Where Winds Meet compatible)
- 🎶 15 Keys Smart Pitch Folding (Heartopia compatible)
- 🔁 Closest Octave Mapping
- 🌍 Global Track Shift
- 🥁 Optional Percussion Removal
- 🧩 Merge Tracks
- 🔀 Merge Channels
- ✂ Trim Leading Silence
- 📂 Batch Processing (Multiple Files / Folder)

---

## 🎯 Key Modes

### 37 Keys
Standard 3-octave mapping used by most game instruments.

### 36 Keys
Same as 37 but without the highest C.

### 15 Keys
Smart pitch folding into 15 keys, no half-notes. Uses scale-degree remapping to preserve harmony.

---

## 🚀 How To Use

1. Open OctaShift
2. Add MIDI file(s) or folder
3. Choose shift method:
   - Closest Octave Mapping
   - Global Track Shift
4. Select key mode (37 / 36 / 15)
5. Enable optional features if needed
6. Click **Process**

Converted files will be saved with a suffix:

```
<Song Name> 37.mid
<Song Name> 36.mid
<Song Name> 15.mid
```

---

## 🛠 Built With

- .NET 10 (LTS)
- WPF
- NAudio (MIDI processing)

---

## ⚠ Notes

- Extremely complex orchestral MIDIs may require manual cleanup.

---

## 📜 License

MIT License  
Free to use, modify, and distribute.

---

🎶 Enjoy converting and performing!

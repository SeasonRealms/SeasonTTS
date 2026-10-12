Platform: android-arm64 (ABI: arm64-v8a; deployed under jniLibs/lib/arm64-v8a)
Android API level: 33 (matches the app's minSdk)
Configuration: Release
QWENTTS_REF: master
GGML_REPO: https://github.com/SeasonRealms/ggml
GGML_REF: master
GGML_MAX_NAME: 128
Android NDK: 28.2.13676358 (r28+ required)
Enabled backends: cpu (single variant, armv8.2-a+dotprod baseline)

Android qwentts artifact contents (arm64-v8a only; no armeabi-v7a / x86 / x86_64 slices, so Android emulators are excluded).
This build compiles ggml with GGML_MAX_NAME=128 (the upstream default is 64) so the ggml_tensor layout matches the other ggml-based runtimes linked into the same app (llama.cpp, stable-diffusion and SeasonGGML, which all use 128).
GGML_BACKEND_DL=OFF: libggml-cpu.so is a plain shared library linked into libggml.so via DT_NEEDED, so the backend registry self-registers at load time (same runtime model as iOS) and nothing has to be scanned or dlopen'd by the app.
CPU targets the armv8.2-a+dotprod baseline (Cortex-A75 class and newer), the same CPU floor the iOS build uses (M1 baseline).
CUDA and Vulkan do not exist on the Android target and are explicitly disabled (qwentts.cpp is transformer-based, so cuDNN is never loaded).
The NDK links 16 KB page-size aligned libraries by default; every shipped .so is re-checked below (Play requirement).

The ggml core libraries (libggml.so, libggml-base.so, libggml-cpu.so) are also produced by the SeasonGGML / llama.cpp / stable-diffusion Android builds from the same ggml ref; ship exactly ONE copy of them inside the APK and let libllama.so / libstable-diffusion.so / libqwen.so resolve it via DT_NEEDED.

Artifact contents:
README.txt
ggml.txt
libggml-base.so
libggml-cpu.so
libggml.so
libqwen.so
qwen.h
qwentts.txt

16 KB page-size alignment (Play requirement, verified after the build):
libggml-base.so: min PT_LOAD align = 0x4000
libggml-cpu.so: min PT_LOAD align = 0x4000
libggml.so: min PT_LOAD align = 0x4000
libqwen.so: min PT_LOAD align = 0x4000

# Third-Party Notices

## ElBruno.QwenTTS

SeasonTTS contains source code derived in part from:

- Project: `ElBruno.QwenTTS`
- Repository: `https://github.com/elbruno/ElBruno.QwenTTS`
- License: MIT
- Copyright: Bruno Capuano

SeasonTTS keeps attribution headers in copied or adapted source files where applicable.

Copied upstream license text:

```text
MIT License

Copyright (c) 2026 Bruno Capuano

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## qwentts.cpp

SeasonTTS wraps the qwentts.cpp native library via P/Invoke and ships pre-built native binaries:

- Project: `qwentts.cpp`
- Repository: `https://github.com/ServeurpersoCom/qwentts.cpp`
- License: MIT
- Copyright: ServeurpersoCom contributors

The managed bindings under `SeasonTTS.GGML` are original work. The native libraries (`qwen.dll` / `libqwen.so` / `libqwen.dylib` / `libqwen-core.a`) are built from the upstream qwentts.cpp source and distributed under the same MIT license.

Pre-built native binaries shipped in `runtimes/` are built via the GitHub Actions workflow at `.github/workflows/build-qwentts.yml` from the [SeasonRealms/qwentts.cpp](https://github.com/SeasonRealms/qwentts.cpp) fork, which tracks upstream.

Copied upstream license text:

```text
MIT License

Copyright (c) 2025 ServeurpersoCom

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Model Distribution Notice

SeasonTTS does not claim ownership of:

- Qwen3-TTS model weights (Apache 2.0, Alibaba / Qwen team)
- tokenizer assets
- converted ONNX model bundles hosted by third parties (elbruno on Hugging Face)
- GGUF model files hosted by third parties (Serveurperso on Hugging Face)

Those assets remain governed by their own upstream terms, repository metadata, and model-specific licensing information.

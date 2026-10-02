# Development instructions

- Develop without activating NYRI on the user's desktop. Do not start the application, call `Application.Run`, show shell windows, or register the shell AppBar for local verification unless the human directly asks to open or test the app.
- Use builds, memory-only checks, offscreen `RenderTargetBitmap` previews and GitHub Actions. Local verification must not change the user's wallpaper, clipboard, audio settings or lock state.
- Continue new modules from the current architecture; preserve existing functionality and avoid repeating completed manual audits.
- Keep the local checkout and prepared EXE in sync. Commit and push significant validated batches to `main`, as authorized by the user.

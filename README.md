# Thorium

Used for https://thorium.ac/rust

**Thorium** is a server-side anti-cheat and analytics mod for [Rust](https://rust.facepunch.com/), implemented as a Harmony-patched .NET library. It runs transparently inside the Rust dedicated server process, requires zero client-side installation, and streams high-fidelity behavioural data to a remote Thorium backend over a persistent WebSocket connection.

---


## Configuration

Thorium reads its configuration from `ThoriumConfig`. The minimum required setting is a **server token** issued by the Thorium platform. Without a valid token, all patches short-circuit immediately and produce no overhead.

---

## Building
Run one of the following:

For building against Release Rust:
```bash
./update-lin-dependencies.bat
```

For building against Staging Rust:
```bash
./update-lin-staging.bat
```

```bash
dotnet build ThoriumRustMod.sln
```

Dependencies (Rust server assemblies) are resolved from the `deps/` directory. Pre-built artifacts are placed in `artifacts/`.

---

## Contributing

Pull requests are welcome. Please keep patches focused — one logical change per PR.

# Product agenda

This agenda covers end-user improvements that are not part of the current
release. Priorities reflect product impact rather than a promised release date.

## In progress — high priority

### Cross-platform end-user releases

Make local icon-pack creation available without development tools on the major
desktop platforms. The v1.2 release work adds self-contained artifacts for
Windows x64/ARM64, macOS Intel/Apple Silicon, and Linux x64/ARM64.

The experience should remain consistent across platforms:

- launch from a downloaded application or executable;
- use the platform Downloads folder as the default destination;
- create one clearly identified `otp-harbor-icons.otphicons` file;
- offer an obvious way to reveal the completed file for transfer to a phone;
- preserve the current pinned-source, hash-validation, local-generation, and
  legal-notice behavior;
- require no SDK, source checkout, command line, or provider-specific tooling;
- publish checksums and platform-specific installation or trust guidance;
- test each published artifact in CI by generating and validating a real pack.

After desktop parity is complete, evaluate a separate mobile-compatible
builder experience for users who do not have a desktop computer. A mobile
builder should remain a distinct package that exports an `.otphicons` file.

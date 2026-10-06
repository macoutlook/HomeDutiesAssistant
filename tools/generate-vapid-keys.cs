// Generates a VAPID key pair for Web Push. Paste the output into .env
// (compose maps it to Push__Vapid__PublicKey / Push__Vapid__PrivateKey / Push__Vapid__Subject).
// Run from the repo root:  dotnet run tools/generate-vapid-keys.cs
// Generate once — rotating the keys invalidates every existing subscription.
using System.Buffers.Text;
using System.Security.Cryptography;

using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var parameters = key.ExportParameters(includePrivateParameters: true);

// Public key = uncompressed EC point: 0x04 || X || Y (65 bytes).
var publicKey = new byte[65];
publicKey[0] = 0x04;
parameters.Q.X!.CopyTo(publicKey, 1);
parameters.Q.Y!.CopyTo(publicKey, 33);

Console.WriteLine($"VAPID_PUBLIC_KEY={Base64Url.EncodeToString(publicKey)}");
Console.WriteLine($"VAPID_PRIVATE_KEY={Base64Url.EncodeToString(parameters.D!)}");
Console.WriteLine("VAPID_SUBJECT=mailto:you@example.com");

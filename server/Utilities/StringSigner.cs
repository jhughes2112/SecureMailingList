#nullable enable
﻿using System;
using System.Security.Cryptography;
using System.Text;

namespace Utilities
{
	// This helper signs a base64 string and produces "base64.base64Signature" output.
	// Generates a temporary RSA keypair on construction. Public key is returned as base64-encoded DER (SubjectPublicKeyInfo).
	public class StringSigner
	{
		private readonly RSA _rsa;

		/// <summary>
		/// Create a signer with a new temporary RSA keypair.
		/// </summary>
		public StringSigner()
		{
			_rsa = RSA.Create();
		}

		/// <summary>
		/// Sign a base64 payload and return "base64.base64Signature".
		/// The signature is RSASSA-PKCS1-v1_5 with SHA-256 over the UTF-8 bytes of the base64 input.
		/// </summary>
		/// <param name="base64">The payload to sign, already base64-encoded.</param>
		/// <returns>Concatenated string: "{base64}.{signatureBase64}". Empty string on error.</returns>
		public string Sign(string base64)
		{
			string result;
			try
			{
				byte[] data = Encoding.UTF8.GetBytes(base64);
				byte[] sig = _rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
				string sigBase64 = Convert.ToBase64String(sig);
				result = base64 + "." + sigBase64;
			}
			catch
			{
				result = string.Empty;
			}

			return result;
		}

		/// <summary>
		/// Get the public key that corresponds to this signer.
		/// Returned format is SubjectPublicKeyInfo (X.509) DER as base64.
		/// </summary>
		/// <returns>Base64 of DER-encoded SubjectPublicKeyInfo. Empty string on error.</returns>
		public string GetPublicKey()
		{
			string key;
			try
			{
				byte[] spki = _rsa.ExportSubjectPublicKeyInfo();
				key = Convert.ToBase64String(spki);
			}
			catch
			{
				key = string.Empty;
			}

			return key;
		}

		/// <summary>
		/// Verify a signed payload of the form "base64.base64Signature" using a public key.
		/// Public key must be SubjectPublicKeyInfo (X.509) DER encoded as base64.
		/// </summary>
		/// <param name="signedData">The concatenated string: "{base64}.{signatureBase64}"</param>
		/// <param name="publicKeyBase64">Base64 of DER-encoded SubjectPublicKeyInfo.</param>
		/// <returns>true if signature is valid; otherwise false.</returns>
		public static bool Verify(string signedData, string publicKeyBase64)
		{
			bool isValid;
			try
			{
				string[] parts = signedData.Split('.');
				if (parts.Length == 2)
				{
					byte[] data = Encoding.UTF8.GetBytes(parts[0]);
					byte[] signature = Convert.FromBase64String(parts[1]);

					using (RSA rsa = RSA.Create())
					{
						byte[] spki = Convert.FromBase64String(publicKeyBase64);
						rsa.ImportSubjectPublicKeyInfo(spki, out int _);
						isValid = rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
					}
				}
				else
				{
					isValid = false;
				}
			}
			catch
			{
				isValid = false;
			}

			return isValid;
		}
	}
}
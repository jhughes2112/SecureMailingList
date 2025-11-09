using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CsvHelper;
using System.Globalization;

namespace SecureMailingList
{
	public class EmailListCSV : IEmailList
	{
		private readonly string _csvFile;

		public EmailListCSV(string csvFile)
		{
			if (string.IsNullOrEmpty(csvFile))
			{
				throw new ArgumentException("CSV file path cannot be null or empty.", nameof(csvFile));
			}
			_csvFile = csvFile;
		}

		public async Task Read(Dictionary<string, EmailEntry> emailList)
		{
			if (File.Exists(_csvFile))
			{
				using (var reader = new StreamReader(_csvFile))
				using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
				{
					await csv.ReadAsync();
					csv.ReadHeader();
					string[]? headers = csv.HeaderRecord;
					if (headers != null && headers.Length >= 2)
					{
						while (await csv.ReadAsync())
						{
							string? email = csv.GetField<string>("email");
							if (string.IsNullOrWhiteSpace(email))
							{
								continue;
							}
							string fullname = csv.GetField<string>("fullname") ?? "";
							List<string> tags = new List<string>();
							for (int i = 2; i < headers.Length; i++)
							{
								string tag = headers[i];
								bool subscribed = csv.GetField<bool>(tag);
								if (subscribed)
								{
									tags.Add(tag);
								}
							}

							if (emailList.ContainsKey(email))
							{
								// Merge tags additively
								foreach (string tag in tags)
								{
									if (!emailList[email].Tags.Contains(tag))
									{
										emailList[email].Tags.Add(tag);
									}
								}
							}
							else
							{
								emailList[email] = new EmailEntry { FullName = fullname, Tags = tags };
							}
						}
					}
				}
			}
		}

		public async Task Write(Dictionary<string, EmailEntry> emailList)
		{
			HashSet<string> allTags = new HashSet<string>();
			foreach (var entry in emailList)
			{
				foreach (string tag in entry.Value.Tags)
				{
					allTags.Add(tag);
				}
			}

			List<string> sortedTags = new List<string>(allTags);
			sortedTags.Sort();

			using (var writer = new StreamWriter(_csvFile))
			using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
			{
				// Write header
				csv.WriteField("email");
				csv.WriteField("fullname");
				foreach (string tag in sortedTags)
				{
					csv.WriteField(tag);
				}
				await csv.NextRecordAsync();

				// Write data
				foreach (var kvp in emailList)
				{
					csv.WriteField(kvp.Key);
					csv.WriteField(kvp.Value.FullName);
					foreach (string tag in sortedTags)
					{
						csv.WriteField(kvp.Value.Tags.Contains(tag));
					}
					await csv.NextRecordAsync();
				}
			}
		}

		public async Task Test()
		{
			// Create test data
			Dictionary<string, EmailEntry> testData = new Dictionary<string, EmailEntry>();
			testData["test@example.com"] = new EmailEntry { FullName = "Test User", Tags = new List<string> { "tag1", "tag2" } };
			testData["another@example.com"] = new EmailEntry { FullName = "Another User", Tags = new List<string> { "tag2", "tag3" } };

			// Write to a temp file
			string tempFile = Path.GetTempFileName();
			try
			{
				EmailListCSV tempCsv = new EmailListCSV(tempFile);
				await tempCsv.Write(testData).ConfigureAwait(false);

				// Read back
				Dictionary<string, EmailEntry> readData = new Dictionary<string, EmailEntry>();
				await tempCsv.Read(readData).ConfigureAwait(false);

				// Check if matches
				if (readData.Count == 2 && readData.ContainsKey("test@example.com") && readData.ContainsKey("another@example.com"))
				{
					EmailEntry entry1 = readData["test@example.com"];
					EmailEntry entry2 = readData["another@example.com"];
					if (entry1.FullName == "Test User" && entry1.Tags.Count == 2 && entry1.Tags.Contains("tag1") && entry1.Tags.Contains("tag2") &&
						entry2.FullName == "Another User" && entry2.Tags.Count == 2 && entry2.Tags.Contains("tag2") && entry2.Tags.Contains("tag3"))
					{
						// PASSED
					}
					else
					{
						throw new Exception("EmailListCSV Test: FAILED - Data mismatch");
					}
				}
				else
				{
					throw new Exception("EmailListCSV Test: FAILED - Read count or key mismatch");
				}

				// Test empty list
				Dictionary<string, EmailEntry> emptyData = new Dictionary<string, EmailEntry>();
				await tempCsv.Write(emptyData).ConfigureAwait(false);
				Dictionary<string, EmailEntry> readEmpty = new Dictionary<string, EmailEntry>();
				await tempCsv.Read(readEmpty).ConfigureAwait(false);
				if (readEmpty.Count == 0)
				{
					// PASSED
				}
				else
				{
					throw new Exception("EmailListCSV Empty Test: FAILED");
				}

				// Test special characters
				Dictionary<string, EmailEntry> specialData = new Dictionary<string, EmailEntry>();
				specialData["special@example.com"] = new EmailEntry { FullName = "Special, User", Tags = new List<string> { "tag,with,commas", "tag with spaces" } };
				await tempCsv.Write(specialData).ConfigureAwait(false);
				Dictionary<string, EmailEntry> readSpecial = new Dictionary<string, EmailEntry>();
				await tempCsv.Read(readSpecial).ConfigureAwait(false);
				if (readSpecial.Count == 1 && readSpecial.ContainsKey("special@example.com"))
				{
					EmailEntry entry = readSpecial["special@example.com"];
					if (entry.FullName == "Special, User" && entry.Tags.Count == 2)
					{
						// PASSED
					}
					else
					{
						throw new Exception("EmailListCSV Special Characters Test: FAILED - Data mismatch");
					}
				}
				else
				{
					throw new Exception("EmailListCSV Special Characters Test: FAILED - Read count or key mismatch");
				}
			}
			finally
			{
				if (File.Exists(tempFile))
				{
					File.Delete(tempFile);
				}
			}
		}
	}
}
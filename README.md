<div align="center">
  <img alt="SecureMailingList" title="SecureMailingList" width="96" src="readme/securemailinglist.png">
  <h1>SecureMailingList</h1>
  <p>A self-contained email signup system that uses RSA signatures and no database.</p>
</div>

---

## Overview

**SecureMailingList** is a minimal, self-hosted email signup and verification service.
It needs no database, timestamp storage, or expiring tokens on disk.

When someone submits a form, you make a request with the following fields:
```
https://yoursite/?r=<base64payload>
```
where your payload should be `email,user name,tag,tag,tag...` where everything is comma separated (you can use double quotes around fields to allow commas in the name, if you want).

The server adds an (optional) expiration timestamp then digitally signs the request then sends an email with a verification link. When a user clicks the link, they return to the server which checks the digital signature and expiration time, then stores their record in the list. There is no need for a full database, just the list of email addresses. Unsubscribe happens when the list of tags is empty.

Note, a new RSA keypair is generated on each boot and never written to disk, so all previous links are invalid after restart. Most transactional emails are pretty short-lived, so I think this is acceptable.

Outgoing mail is currently only supported via SendGrid API key.  It is trivial to add other providers, however.

---

## Features

* **No Database** — Everything is verified cryptographically and appended directly.
* **Ephemeral RSA Signing** — Links prove origin without storing pending data.
* **Optional Expiration** — Links can automatically reject if older than N seconds.
* **Simple Outputs** — CSV file, simple as it gets. There is also a query to download the file straight from the server.
* **Zero Category Configuration** — Just supply tags on the request call, they get added automatically.

---

## Quick Start

Assuming you have Docker installed:

1. build.bat
2. run.bat
3. Double-click signup.html so it opens in a browser.
4. Form submissions call into http://localhost:18888/ and trigger an email (assuming you have configured a SendGrid API key in run.bat).
5. When you click the link in your email, it should take you to a very simple browser page that says your email is signed up for specific tags, or has unsubscribed.
6. Unsubscribe is the same as subscribe, only with no tags. It still requires you to click the link in an email to unsubscribe.
7. If you set the download_password option, hit this link in a browser to download the current email list:
```
http://localhost:18888/?d=yourpassword
```
---

## Email Configuration File

The `--email_cfg` option specifies a text file with exactly 5 lines:

1. Path to the plain text email template file.
2. Path to the HTML email template file.
3. Email subject line.
4. From email address.
5. From display name.

Example `email.cfg`:

```
/data/plain.txt
/data/html.html
Confirm your email
support@yoursite.com
Your Site Name
```

Note, you can use these tags to substitute in data into the email templates: `{{LINK}}`, `{{FROMNAME}}`, `{{FROMEMAIL}}`, `{{USERNAME}}`.

---

## Configuration

| Option                 | Required | Default                 | Description                                                                                       |
| ---------------------- | -------- | ----------------------- | ------------------------------------------------------------------------------------------------- |
| `--conn_bindurl`       | No       | `http://+:18888/`       | Bind address for the web server. `/health` and `/metrics` always available.                       |
| `--hosted_url`         | Yes      |                         | URL where this server can be reached when a link is clicked. Such as https://example.com/signup   |
| `--email_cfg`          | Yes      |                         | Path to email configuration file (see above).                                                     |
| `--sendgrid_apikey`    | Yes      |                         | SendGrid API key for sending emails.                                                              |
| `--csvfile`            | Yes      |                         | Path to CSV file local CSV file storage.                                                          |
| `--download_password`  | No       |                         | If provided, this is the password that allows remote download of the email list in your browser.  |
| `--link_valid_seconds` | No       | `86400`                 | Maximum age (in seconds) a signed link remains valid. `0` disables expiration.                    |

---

## Endpoints

* **Health & Metrics**

  * `/health` — Basic liveness probe.
  * `/metrics` — Prometheus metrics.

* **Signup**

  * `?r=<base64url(csv)>` — Requests an email sent to the user.
  * `?v=<base64url(csv).base64url(signature)>` — Verifies the user's preferences after clicking the emailed link.

* **List Retrieval**
  * `?d=<password>` — If this matches your download_password CLI parameter, it automatically downloads the full file to your browser.

Also note, hosting this on subpaths is totally fine.  Just put the full path in `--hosted_url`, whatever that is.

---

## Limitations

* RSA keys regenerate on each boot; old links immediately fail.
* Expiration is checked purely by timestamp inside the payload.
* There is no resend or recovery mechanism. Just go back to the form and re-apply settings, even for unsubscribe.
* The CSV file is fully rewritten on each update, ensuring deduplication.

---

## Contributing

Pull requests and issues are welcome.

---

## License

Licensed under the **MIT No Attribution** license. See [LICENSE](./LICENSE).

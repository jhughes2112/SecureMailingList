docker run -it --rm  --name securemailinglist -p 18888:18888 ^
  -v %cd%/data:/data -w /data ^
  securemailinglist:latest ^
  --conn_bindurl http://+:18888/ ^
  --hosted_url http://localhost:18888/ ^
  --email_cfg /app/static_root/email.cfg ^
  --sendgrid_apikey API_KEY_GOES_HERE ^
  --csvfile /data/emails.csv ^
  --download_password changethis
  
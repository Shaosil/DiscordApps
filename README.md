<h1>ShaosilBot</h1>

Instead of using the basic JS libraries as referenced by Discord's docucmentation, this project uses the Discord.NET package. This branch uses an ASP Core app, which can be hosted on the server of your choice. Personally, I'm using docker on my local machine, with dynu for a public URL and Caddy for automatic certificate handling.

Note: Currently, this bot is mostly built to target a SINGLE server (guild) per hosting instance. Most commands may work if it's a part of multiple servers, but some will most likely break altogether.

<h2>Requirements</h2>

<ul>
	<li>Docker</li>
</ul>

Yep, I believe that's all you need! Plus a healthy amount of basic knowledge of how to run a docker container never hurt.

<h2>Running/Local Debug Requirements</h2>

First of all, I assume you plan on using this code for your own bot, because running it requires you to have a lot of security and setup keys that I can't share. That being said, here is everything you should have prepared before being able to debug this app.

<ul>
	<li>If you are going to host it yourself, then unless you have a static IP, you need to use some kind of DNS provider that keeps your dynamic IP tied to a fixed address. In my case, I'm using <a href="https://www.dynu.com/">dynu.com</a> for that purpose. Create an account, use one of their free domains, and download their IP Update Client software to keep your IP synced to your registered domain.</li>
	<li>I <i>think</i> Discord requires https for the endpoint, so you'll also need to go ahead and generate a certificate. The easiest way I could find for that to be automatically handled was to use <a href="https://caddyserver.com/">Caddy</a> as my one-stop-shop for generating and automatically renewing a valid certificate.</li>
	<li>Set up your own <a href="https://discord.com/developers/docs/getting-started#creating-an-app">Discord application/bot.</a> Don't follow the instructions they provide for setting up your project with Glitch and the JS libraries. We'll get to hosting down below.</li>
	<li>Depending on what your bot will do and what permissions you gave it, you may need to enable some priviledged gateway intents on the bot page.</li>
	<li>Make a copy of <b>appsettings.example.json</b>, rename it to <b>appsettings.json</b>, and fill out any values for extra features you plan to use.</li>
  <li>Your best bet is to make a <b>compose.yaml</b> file in this directory with the following contents. Feel free to change your ./data and ./logs directories:</li>

```yaml
services:
  shaosilbot:
    build: ./
    image: shaosilbot:local
    container_name: shaosilbot
    restart: unless-stopped
    stop_grace_period: 30s
    volumes:
      - ./data:/app/data
      - ./logs:/app/logs

# Uncomment the following if you plan to use Caddy for HTTPS
#     networks:
#       - caddy_gateway

# networks:
#   caddy_gateway:
#     external: true
```

  <li>If you run that compose file (docker compose up), the bot will be running but not yet reachable. More on that below.</li>
</ul>

<h2>Making It Reachable</h2>

If you've been following all of the steps, and you want to host it the same way I do (aka probably the easiest way), you should have set up a dynu account by now and have a free public URL that points to your IP. To make sure your PC can receive secure requests, you need to follow these next steps:

<ul>
  <li>Open ports 80 and 443 on your router. These are used by Caddy to retrieve certificates for you.</li>
  <li>Run `docker network create caddy_gateway` to create a shared network that docker containers can use to communicate with each other.</li>
  <li>Make a "caddy" folder somewhere and create two files in it: <b>compose.yaml</b> and <b>caddySettings</b>. Naming is important.</li>
  <li>In the contents of caddySettings, paste the folowing, obviously replacing the placeholder URL with your own:</li>

```
<your bot's public URL> {
    reverse_proxy shaosilbot:5000
}
```

  <li>In the compose.yaml file that you just made in the same folder, paste the following:</li>

```yaml
services:
  caddy:
    image: caddy:latest
    container_name: caddy
    restart: unless-stopped
    ports:
      - "443:443"
      - "80:80"
    volumes:
      - ./caddySettings:/etc/caddy/Caddyfile
      - caddy_data:/data
      - caddy_config:/config
    networks:
      - caddy_gateway

networks:
  caddy_gateway:
    external: true

volumes:
  caddy_data:
  caddy_config:
```

  <li>Back in the ShaosilBot's compose.yaml, make sure the networking sections are all uncommented.</li>
  <li>Finally, run `docker compose up` in the caddy directory, and again in your ShaosilBot directory if you haven't since uncommenting the networking parts.</li>
</ul>

<h2>Conclusion</h2>

Now in theory you should be able run it, and the bot would even show as online and have registered slash commands on your server(s). However, those slash commands punch out to your bot's Interactions Endpoint URL. If you haven't yet, go ahead and set your bot's URL to https://(Your-URL)/interactions.

When you click "Save Changes", Discord sends two challenge requests to the URL you specified. As long as the application is running and you've set everything up correctly, the application should verify the signatures and respond accordingly. Once it does, Discord will give you a success message on the webpage. If something failed, check the application's logs in the debug command window and see what went wrong.

That's about it! The only other thing to note is that there are still many instances where I've hardcoded user and channel IDs. This will slowly improve and move to a configuration based structure. And keep in mind that the Twitch and ChatGPT commands are not covered in this guide and require separate external configuration.

<b>Happy botting!</b>
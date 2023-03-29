<h1>ShaosilBot</h1>

Instead of using the basic JS libraries as referenced by Discord's docucmentation, this project uses the Discord.NET package. This branch uses an ASP Core app, which can be hosted on the server of your choice. Personally, I'm using IIS on my local machine.

Note: Currently, this bot is mostly built to target a SINGLE server (guild) per hosting instance. Most commands may work if it's a part of multiple servers, but some will most likely break altogether.

<h2>Compile Requirements</h2>
<ul>
	<li>Visual Studio 2022</li>
</ul>

Yep, I believe that's all you need to actually do the compiling. Now _running it_ is a different story.

<h2>Running/Local Debug Requirements</h2>

First of all, I assume you plan on using this code for your own bot, because running it requires you to have a lot of security and setup keys that I can't share. It would also be <i>extremely beneficial</i> if you have prior experience hosting a site somewhere. That being said, here is everything you should have prepared before being able to debug this app.

<ul>
	<li>If you are going to host it yourself, then unless you have a static IP, you need to use some kind of DNS provider that keeps your dynamic IP tied to a fixed address. In my case, I'm using <a href="https://www.dynu.com/">dynu.com</a> for that purpose. Create an account, use one of their free domains, and download their IP Update Client software to keep your IP synced to your registered domain.</li>
	<li>I <i>think</i> Discord requires https for the endpoint, so you'll also need to go ahead and generate a certificate. I won't go into the details for that, but I used <a href="https://www.win-acme.com/">win-acme</a> as my one-stop-shop for generating, binding to IIS, and auto renewing. And don't forget to configure IIS's SSL, HTTPS, and HSTS settings!</li>
	<li>Set up your own <a href="https://discord.com/developers/docs/getting-started#creating-an-app">Discord application/bot.</a> Don't follow the instructions they provide for setting up your project with Glitch and the JS libraries. We'll get to hosting down below.</li>
	<li>Depending on what your bot will do and what permissions you gave it, you may need to enable some priviledged gateway intents on the bot page.</li>
	<li>Add a new JSON file underneath the ShaosilBot.Web project, and call it <b>appsettings.json</b>.<img src="https://user-images.githubusercontent.com/12295139/225418523-29958b64-43cd-49fe-87b8-3035f25b070a.png"></img></li>
	<li>You will need to add the following keys and values to said file:</li>
	
```json
{
    "Logging": {
        "LogLevel": {
            "Default": "Information",
            "Microsoft.AspNetCore": "Information"
        }
    },
    "AllowedHosts": "*",
	
    "TargetGuild": "<Your guild ID. Certain services can only target a single guild at the moment>",
    "MainChannel": "<The main/general channel. Currently only used with ChatGPT announcements>",
    "FilesBasePath": "<The path where the bot will read/write certain files. More on this below. Make sure your IIS user has permissions to the folder>",
    "UtilitiesAuthToken": "<Used to authenticate external pings to the Utilities controller. You can generate your own token for this.>",
    "PublicKey": "<Your bot's application ID. You can get this from your Discord portal>",
    "BotToken": "<Your bot token - You can get this when setting up your bot in the Discord portal>",
    "RandomWowURL": "https://owen-wilson-wow-api.onrender.com/wows/random",
    "ImgurClientID": "<Only needed if using the /git-blame command>",
    "ImgurGitBlameAlbum": "<The Imgur API album URL to pull blame images from. Only needed if using the /git-blame command. My current images are at https://imgur.com/a/1IzijHj>",

    "OpenAIAPIKey": "<Only needed if integrating with ChatGPT>",
    "OpenAIOrganization": "<Only needed if integrating with ChatGPT>",
    "ChatGPTEnabled": "false (Unless you are integrating with ChatGPT)",
    "ChatGPTSystemMessage": "<The setup prompt used in each ChatGPT request - Only needed if integrating with ChatGPT>",
    "ChatGPTMessageTokenLimit": "<A hard limit on ChatGPT's response length - Only needed if integrating with ChatGPT>",
    "ChatGPTMonthlyTokenLimit": "<The max amount of tokens your bot should use in a month. Used for rate limiting calculations per hour>",
    "ChatGPTMessagePairsToKeep": "<How many prompt/response pairs per channel to keep in local history for sending in each request. 0-3 recommended>",

    "TwitchClientID": "<Only needed if using the bot to announce twitch streams>",
    "TwitchClientSecret": "Only needed if using the bot to announce twitch streams>",
}
```
</ul>

<h2>Conclusion</h2>

Now in theory you should be able run it, and the bot would even show as online and have registered slash commands on your server(s). However, those slash commands punch out to your bot's Interactions Endpoint URL. If you haven't yet, go ahead and set your bot's URL to https://(Your-URL)/interactions.

When you click "Save Changes", Discord sends two challenge requests to the URL you specified. As long as the application is running and you've set everything up correctly, the application should verify the signatures and respond accordingly. Once it does, Discord will give you a success message on the webpage. If something failed, check the application's logs in the debug command window and see what went wrong.

That's about it! The hardest part is the hosting bit, of which I realize I've left out many details. But IIS and HTTPS are their own topics. One day I may come back and update with specific instructions on how to do that, but I'm sure if you've got this far you can handle it with a bit of self research. :)

The only other thing to note is that there are still many instances where I've hardcoded user and channel IDs. This will slowly improve and move to a configuration based structure. And keep in mind that the Twitch and ChatGPT commands are not covered in this guide and require separate external configuration.

<b>Happy botting!</b>
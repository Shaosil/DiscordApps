<h1>ShaosilBot</h1>

Instead of using the basic JS libraries as referenced by Discord's docucmentation, I decided to go the full Microsoft Stack instead, like a crazy person.

<h2>Compile Requirements</h2>
<ul>
	<li>Visual Studio 2022</li>
</ul>

Yep, I believe that's all you need to actually do the compiling. Now _running it_ is a different story.

<h2>Running/Local Debug Requirements</h2>

First of all, I assume you plan on using this code for your own bot, because running it requires you to have a lot of security and setup keys that I can't share. It would also be <i>extremely beneficial</i> if you have prior experience with Azure Functions or App Services. That being said, here is everything you should have prepared before being able to debug this app.

<ul>
	<li>Make an account on <a href="ngrok.com">ngrok.com</a>, then follow the <a href="https://dashboard.ngrok.com/get-started/setup">instructions</a> for downloading and connecting to your account. More on this later.</li>
	<li>Set up your own <a href="https://discord.com/developers/docs/getting-started#creating-an-app">Discord application/bot.</a> Don't follow the instructions they provide for setting up your project with Glitch and the JS libraries. We'll get to hosting down below.</li>
	<li>Depending on what your bot will do and what permissions you gave it, you may need to enable some priviledged gateway intents on the bot page.</li>
	<li>Add a new settings file underneath the ShaosilBot project, and call it <b>local.settings.json</b>.<img src="https://user-images.githubusercontent.com/12295139/169352687-3f5d5982-e97e-4fad-9083-cbf6531cce28.png"></img></li>
	<li>You will need to add the following keys and values to said file:</li>
	
```json
{
  "IsEncrypted": false,
  "Values": {
	"AzureWebJobsStorage": "UseDevelopmentStorage=true",
	"FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
	"FUNCTIONS_EXTENSION_VERSION": "~4",
	"APPLICATIONINSIGHTS_CONNECTION_STRING": "",
	"ApplicationInsightsAgent_EXTENSION_VERSION": "~2",
	"DiagnosticServices_EXTENSION_VERSION": "~3",
	"APPINSIGHTS_PROFILERFEATURE_VERSION": "1.0.0",

	"ClientID": "<YOUR BOT'S CLIENT/APP ID>",
	"PublicKey": "<YOUR BOT'S PUBLIC KEY>",
	"BotToken": "<YOUR BOT'S PUBLIC SECRET (DO NOT SHARE!)>",

	"RandomWowURL": "https://owen-wilson-wow-api.herokuapp.com/wows/random",
	"ImgurClientID": "<IF YOU WANT /git-blame TO WORK, CREATE AN IMGUR APP AND USE ITS CLIENT ID HERE>",
	"ImgurGitBlameAlbum": "https://api.imgur.com/3/album/1IzijHj/images",
	"TwitchClientID": "<MY TWITCH APP'S CLIENT ID. YOU MAY IGNORE THIS>",
	"TwitchClientSecret": "<MY TWITCH APP'S CLIENT SECRET. YOU MAY IGNORE THIS>",
	"TwitchAPISecret": "<MY TWITCH APP'S API SECRET. YOU MAY IGNORE THIS>"
  }
}
```

</ul>

Now in theory you should be able run it, and the bot would even show as online and have registered slash commands on your server(s). However, those slash commands punch out to your bot's Interactions Endpoint URL, which you haven't set up yet if you've been following these instructions.

Go ahead and open a command or powershell window and navigate to wherever you extracted ngrok.exe from above, then run this command: `ngrok.exe http localhost:3000`

If you've followed the configuration instructions from their website, you should now see a screen similar to the following:
<img src="https://user-images.githubusercontent.com/12295139/169361191-aa6c7839-2c42-41ff-b476-b878b8112ab3.png"></img>

When connected to your account, ngrok takes requests hitting the address next to the "forwarding" URL and as long as the exe is running, it forwards those to the address you specified in the command you just ran, so in our case, localhost:3000. This means Discord is effectively pushing API calls to your local machine, so you can catch them while debugging! Pretty nifty.

The final step is to actually tell Discord to hit ngrok's URL. It's different each time you run it, so you will have to repeat this step each time you run ngrok.exe while developing.

<ul>
	<li>Copy the forwarding ngrok URL from the command window (the one ending in ".io") and go to your bot's <b>General Information</b> tab. Now paste that URL into the <b>Interactions Endpoint URL</b> textbox, making sure to append "/interactions" to the end so it goes to the correct application endpoint.</li>
</ul>

When you click "Save Changes", Discord sends two challenge requests to the URL you specified. As long as the application is running and you've set everything up correctly, the application should verify the signatures and respond accordingly. Once it does, Discord will give you a success message on the webpage. If something failed, check the application's logs in the debug command window and see what went wrong.

<i>Final note on debugging: You may want to remove the CatFactsTimer related code, if it still exists. It relies on having an Azure storage account set up (locally or otherwise) for the triggers and subscriber data, and will most likely throw exceptions each time it triggers.</i>

<h2>FREE Hosting</h2>

If you made it this far, congratulations, your Discord bot is alive and running! So long as the application is running on your machine, that is. You most likely don't want the overhead of having Visual Studio debugging in the background constantly, so let's talk about hosting options. Well, one option specifically. <b>Microsoft Azure Functions</b>. When used in a small enough environment and certain deployment measures are taken, the cost should typically be $0 per month. Keep in mind that you can sign up for a free Azure account with $200 bonus credits (that expire in a month) to experiment, but after that you will lose access to most features unless you set up a payment option and upgrade to a pay-as-you-go plan. But again, the idea is to pay $0 as you go. :)

Setting up Azure services is a beast in itself, and I will most certainly be not going over the fine details of all that. The best I can do is link the following documentation on the basics:

<ul>
	<li>Register for a free Azure account <a href="https://azure.microsoft.com/en-us/free/">here<a></li>
</ul>
		
... You know what, I've been typing at this for some time now and I just realized the hosting details are way more detailed than I remembered, so I'm going to take a break and (probably) update it in the near future. I will say if you want it to stay free, you have to deploy it via an externally-hosted zip file, and as far as I know this must be done via Azure's Command Line Interface (CLI). Personally I'm using a GitHub workflow file which you can inspect if needed, which uploads the published zip file to blob storage and then restarts the functions app, which automatically checks for a new zip and deploys itself.

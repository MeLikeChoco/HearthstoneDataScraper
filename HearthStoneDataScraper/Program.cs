using System;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Concurrent;
using System.Net.Http;
using AngleSharp.Parser.Html;
using Newtonsoft.Json;
using System.Threading;
using AngleSharp.Dom;
using System.Text.RegularExpressions;

//I really like Parallel.ForEach, maybe I'm using it unncessarily...

namespace HearthStoneDataScraper
{

    class Program
    {

        //didn't want to put static in front of everything
        internal static void Main(string[] args)
            => new Program().Run();

        internal const string BaseUrl = "http://hearthstone.gamepedia.com";
        internal readonly string[] CardLists = new string[]
        {

            "/Basic_card_list",
            "/Classic_card_list",
            "/Naxxramas_card_list",
            "/Goblins_vs_Gnomes_card_list",
            "/Blackrock_Mountain_card_list",
            "/The_Grand_Tournament_card_list",
            "/The_League_of_Explorers_card_list",
            "/One_Night_in_Karazhan_card_list",
            "/Mean_Streets_of_Gadgetzan_card_list",
            "/Journey_to_Un%27Goro_card_list",
            "/Hall_of_Fame_card_list",

        };
        internal readonly ParallelOptions POptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 10 };

        internal HttpClient _web;
        internal HtmlParser _parser;
        internal ConcurrentBag<Card> _cards;

        internal ConcurrentBag<string> _collectibles;
        internal ConcurrentBag<string> _uncollectibles;
        //internal ConcurrentBag<string> _collectiblesErrorList;
        //internal ConcurrentBag<string> _uncollectiblesErrorList;

        internal void Run()
        {

            Log("Hearthstone Data Scraper has begun.");

            _cards = new ConcurrentBag<Card>();
            _collectibles = new ConcurrentBag<string>();
            _uncollectibles = new ConcurrentBag<string>();
            _web = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            _parser = new HtmlParser();

            GetListOfCards();
            GetCards(_collectibles, true);
            GetCards(_uncollectibles, false);

            Log("Writing to Database.json");
            File.WriteAllText("Database.json", JsonConvert.SerializeObject(_cards, Formatting.Indented));
            Log("Finished writing to Database.json");
            Log($"There are {_cards.Count} cards.");
            Log("Hearthstone Data Scraper has finished. Press any key to exit...");

            Console.ReadKey();

        }

        internal void GetCards(ConcurrentBag<string> cards, bool isCollectible)
        {

            var typeOfCards = isCollectible ? "collectibles" : "uncollectibles";
            var counter = 1;
            var size = cards.Count;

            Log($"Getting {typeOfCards}...");

            Parallel.ForEach(cards, POptions, link =>
            {

                var dom = _parser.ParseAsync(_web.GetStreamAsync(link).Result).Result;
                var mainDom = dom.GetElementById("mw-content-text");
                var cardInfo = mainDom.Children.First();
                var images = cardInfo.GetElementsByTagName("img").Select(image => image.GetAttribute("srcset") ?? image.GetAttribute("src")); //flatten list
                var stats = cardInfo.GetElementsByClassName("body").First().GetElementsByTagName("tr");
                var descLore = cardInfo.GetElementsByTagName("p");

                var card = new Card
                {

                    Name = cardInfo.FirstElementChild.TextContent,
                    RegularImage = GetImageSrc(images.First()),
                    GoldImage = GetImageSrc(images.ElementAtOrDefault(1) ?? "N/A"),
                    Collectability = isCollectible ? Status.Collectible : Status.Uncollectible,
                    Artist = GetArtist(mainDom),
                    Url = BaseUrl + link,

                };

                //all tag names are capitalized in anglesharp
                if (!string.IsNullOrEmpty(descLore.First().GetAttribute("style")))
                {

                    card.Description = descLore.First().TextContent;
                    var possibleLore = descLore[1];

                    if (possibleLore.FirstElementChild.TagName != "A")
                        card.Lore = descLore[1].TextContent;

                }
                else
                    card.Lore = descLore.First().TextContent;

                foreach (var statLine in stats)
                {

                    var titleValue = statLine.Children;
                    var title = titleValue.First().TextContent.Trim();
                    var value = titleValue[1].TextContent.Trim();

                    switch (title)
                    {

                        case "Set:":
                            card.Set = value;
                            break;
                        case "Type:":
                            card.Type = value;
                            break;
                        case "Class:":
                            card.Class = value;
                            break;
                        case "Rarity:":
                            card.Rarity = value;
                            break;
                        case "Cost:":
                            card.ManaCost = value;
                            break;
                        case "Attack:":
                            card.Attack = value;
                            break;
                        case "Health:":
                            card.Health = value;
                            break;
                        case "Durability":
                            card.Durability = value;
                            break;
                        case "Abilities:":
                            card.Abilities = value.Split(new string[] { ", " }, StringSplitOptions.None);
                            break;
                        case "Tags:":
                            card.Tags = value.Split(new string[] { ", " }, StringSplitOptions.None);
                            break;

                    }

                }

                _cards.Add(card);

                InlineLog($"{Interlocked.Increment(ref counter)}/{size}");

            });

            Log($"Finished getting {typeOfCards}.");

        }

        //no regex has worked out for me and im not going to try to formulate my own expression so I did it manually
        internal string GetImageSrc(string srcSet)
        {

            var qualityLink = srcSet.Split(',').ElementAtOrDefault(1)?.Trim();

            if (string.IsNullOrEmpty(qualityLink))
                return srcSet;
            else
                return qualityLink.Split(' ').First();

        }

        internal string GetArtist(IElement dom)
        {

            var artistTitle = dom.GetElementsByTagName("h2").FirstOrDefault(element => element.TextContent.Contains("Artist"));

            if (artistTitle == null)
                return null;

            var artistTitleIndex = dom.Children.ToList().IndexOf(artistTitle);
            return dom.Children.ElementAt(artistTitleIndex + 1).FirstElementChild.TextContent;

        }

        internal void GetListOfCards()
        {

            Log("Amassing card list...");

            var counter = 1;

            Parallel.ForEach(CardLists, POptions, expansion =>
            {

                var dom = _parser.ParseAsync(_web.GetStreamAsync(expansion).Result).Result;
                var mainDom = dom.GetElementById("mw-content-text");
                var tables = mainDom.GetElementsByTagName("table");
                var collectibleCards = tables[0].GetElementsByTagName("tbody").First().Children.Where(element => !element.TextContent.Contains("Description") && !element.TextContent.Contains("Showing all"));
                var uncollectibleCards = tables[1].GetElementsByTagName("tbody").First().Children.Where(element => !element.TextContent.Contains("Description") && !element.TextContent.Contains("Showing all"));

                foreach (var card in collectibleCards)
                {

                    var cardUrl = card.GetElementsByTagName("a").First().GetAttribute("href");

                    //if (!string.IsNullOrEmpty(cardUrl))
                    _collectibles.Add(cardUrl);

                    InlineLog($"{Interlocked.Increment(ref counter)}");

                }

                foreach (var card in uncollectibleCards)
                {

                    var cardUrl = card.GetElementsByTagName("a").First().GetAttribute("href");

                    //if (!string.IsNullOrEmpty(cardUrl))
                    _uncollectibles.Add(cardUrl);

                    InlineLog($"{Interlocked.Increment(ref counter)}");

                }

            });

            //var dom = await _parser.ParseAsync(await _web.GetStreamAsync(ListOfCards[0]));
            //var mainDom = dom.GetElementById("mw-content-text");
            //var tables = mainDom.GetElementsByTagName("table");
            //var collectibleCards = tables[0].GetElementsByTagName("tbody").First().Children.Where(element => !element.TextContent.Contains("Description") && !element.TextContent.Contains("Showing all"));
            //var uncollectibleCards = tables[1].GetElementsByTagName("tbody").First().Children.Where(element => !element.TextContent.Contains("Description") && !element.TextContent.Contains("Showing all"));

            //Parallel.ForEach(collectibleCards, card =>
            //{

            //    var cardUrl = card.GetElementsByTagName("a").First().GetAttribute("href");

            //    if (!string.IsNullOrEmpty(cardUrl))
            //        _collectibles.Add(cardUrl);

            //});

            //Parallel.ForEach(uncollectibleCards, card =>
            //{

            //    var cardUrl = card.GetElementsByTagName("a").First().GetAttribute("href");

            //    if (!string.IsNullOrEmpty(cardUrl))
            //        _uncollectibles.Add(cardUrl);

            //});

            Log("Finished amassing card list.");

        }

        internal void Log(string message)
            => Console.WriteLine($"{DateTime.Now} {message}");

        internal void InlineLog(string message)
            => Console.Write($"{DateTime.Now} {message}\r");

    }

}
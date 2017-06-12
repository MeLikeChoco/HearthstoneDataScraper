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
using AngleSharp.Dom.Html;
using System.Collections.Generic;

//I really like Parallel.ForEach, maybe I'm using it unncessarily...

namespace HearthStoneDataScraper
{

    class Program
    {

        //didn't want to put static in front of everything
        internal static void Main(string[] args)
            => new Program().Run();

        internal const string BaseUrl = "http://hearthstone.gamepedia.com";
        internal const string WhiteSpacePattern = @"[^\S\r\n]+";
        internal const string HtmlTagPattern = @"<[^>]*>";
        internal readonly ParallelOptions POptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount / 2 * 10 };
        //internal readonly ParallelOptions POptions = new ParallelOptions { MaxDegreeOfParallelism = 1 };
        internal readonly string[] AlphabeticalSortedCards = new string[]
        {

            "/index.php?title=Category:All_cards",
            "/index.php?title=Category:All_cards&pagefrom=Beneath+the+Grounds#mw-pages",
            "/index.php?title=Category:All_cards&pagefrom=Cloaked+Huntress#mw-pages",
            "/index.php?title=Category:All_cards&pagefrom=Dismount#mw-pages",
            "/index.php?title=Category:All_cards&pagefrom=Fen+Creeper#mw-pages",
            "/index.php?title=Category:All_cards&pagefrom=Grimestreet+Enforcer#mw-pages",
            "/index.php?title=Category:All_cards&pagefrom=Jade+Golem+%2827%2F27%29#mw-pages",
            "/index.php?title=Category:All_cards&pagefrom=Magic+Mirror#mw-pages",
            "/index.php?title=Category:All_cards&pagefrom=Nerubian+Egg#mw-pages",
            "/index.php?title=Category:All_cards&pagefrom=Ragnaros+the+Firelord#mw-pages",
            "/index.php?title=Category:All_cards&pagefrom=Shaku%2C+the+Collector#mw-pages",
            "/index.php?title=Category:All_cards&pagefrom=Stonesculpting+%28Normal%29#mw-pages",
            "/index.php?title=Category:All_cards&pagefrom=Twisted+Light#mw-pages",

        };
        internal readonly string[] CardLists = new string[]
        {

            "/Basic_card_list",
            "/Classic_card_list",
            "/Naxxramas_card_list",
            "/Goblins_vs_Gnomes_card_list",
            "/Blackrock_Mountain_card_list",
            "/The_Grand_Tournament_card_list",
            "/The_League_of_Explorers_card_list",
            "/Whispers_of_the_Old_Gods_card_list",
            "/One_Night_in_Karazhan_card_list",
            "/Mean_Streets_of_Gadgetzan_card_list",
            "/Journey_to_Un%27Goro_card_list",
            "/Hall_of_Fame_card_list",
            "/Debug_card",

        };

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
                var cardInfo = mainDom.GetElementsByTagName("div").First();
                var images = cardInfo.GetElementsByTagName("img").Select(image => image.GetAttribute("srcset") ?? image.GetAttribute("src")); //flatten list
                var stats = cardInfo.GetElementsByClassName("body").FirstOrDefault()?.GetElementsByTagName("tr");
                var descLore = cardInfo.GetElementsByTagName("p");

                var card = new Card
                {

                    Name = dom.GetElementById("firstHeading").TextContent.Trim(),
                    RegularImage = GetImageSrc(images.FirstOrDefault()),
                    GoldImage = GetImageSrc(images.ElementAtOrDefault(1)),
                    FullArt = GetFullArt(mainDom),
                    Collectability = isCollectible ? Status.Collectible : Status.Uncollectible,
                    Artist = GetArtist(mainDom),
                    Url = BaseUrl + link,

                };

                //if it doesn't pass this check, it doesn't have a description anyway
                if (descLore.Length != 0)
                {

                    if (!string.IsNullOrEmpty(descLore.First().GetAttribute("style")))
                    {

                        var description = descLore.First().InnerHtml.Replace("<br>", "\n");

                        description = Regex.Replace(description, WhiteSpacePattern, " ");
                        card.Description = Regex.Replace(description, HtmlTagPattern, "");
                        var possibleLore = descLore.ElementAtOrDefault(1);

                        if (possibleLore != null && possibleLore.FirstElementChild.TagName != "A")
                            card.Lore = Regex.Replace(descLore[1].TextContent.Trim(), WhiteSpacePattern, " ");

                    }
                    else if (!descLore.First().TextContent.Contains("Hearthpwn"))
                        card.Lore = Regex.Replace(descLore.First().TextContent.Trim(), WhiteSpacePattern, " ");

                }

                if (stats != null)
                {

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
                                Enum.TryParse<Type>(value, out var cardType);
                                card.Type = cardType;
                                break;
                            case "Subtype":
                                Enum.TryParse<Race>(value, out var race);
                                card.Race = race;
                                break;
                            case "Class:":
                                card.Class = value;
                                break;
                            case "Rarity:":
                                Enum.TryParse<Rarity>(value, out var rarity);
                                card.Rarity = rarity;
                                break;
                            case "Cost:":
                                card.ManaCost = value;
                                break;
                            case "Attack:":
                                card.Attack = value;
                                break;
                            case "Health:":
                                card.Health = Regex.Replace(value.Replace(")", ") "), WhiteSpacePattern, " ");
                                break;
                            case "Durability:":
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

                }

                card.Aquisition = GetAquisition(mainDom, card.Type);
                card.Bosses = GetBosses(mainDom);

                _cards.Add(card);

                InlineLog($"{Interlocked.Increment(ref counter)}/{size}");

            });

            Log($"Finished getting {typeOfCards}.");

        }

        internal string GetBosses(IElement mainDom)
        {

            var bossTitle = mainDom.GetElementsByTagName("h2").FirstOrDefault(element => element.TextContent.Contains("Bosses"));

            if (bossTitle == null)
                return null;

            var children = mainDom.Children;
            var artistTitleIndex = children.ToList().IndexOf(bossTitle) + 1;
            var bosses = new List<string>(2);
            bool gotDiv = false;

            for (int i = artistTitleIndex; i > -1; i++)
            {

                var element = children[i];

                if (element.TagName == "DIV")
                {

                    if (!gotDiv)
                        gotDiv = true;

                    bosses.Add(element.FirstElementChild.GetAttribute("title"));

                }
                else if (gotDiv)
                    break;

            }

            return string.Join(", ", bosses);

        }

        internal string GetAquisition(IElement mainDom, Type cardType)
        {

            switch (cardType)
            {

                case Type.Spell:
                case Type.Weapon:
                    var element = mainDom.GetElementsByTagName("h2").FirstOrDefault(e => e.TextContent.Contains("How to get"));

                    if (element == null)
                        return null;

                    var children = mainDom.Children;
                    var index = children.ToList().IndexOf(element) + 1;
                    IElement nextElement;
                    string aquisition = "";

                    do
                    {

                        aquisition += children[index].TextContent;
                        index++;
                        nextElement = children[index];

                    } while (nextElement.TagName == "P");

                    return aquisition.Trim();
                case Type.Minion:
                    element = mainDom.GetElementsByTagName("h2").FirstOrDefault(e
                        => e.TextContent.Contains("Summoned by")
                        || e.TextContent.Contains("Transformed by")
                        || e.TextContent.Contains("How to get"));

                    if (element == null)
                        return null;

                    children = mainDom.Children;
                    index = children.ToList().IndexOf(element) + 1;
                    aquisition = "";

                    do
                    {

                        aquisition += children[index].TextContent;
                        index++;
                        nextElement = children[index];

                    } while (nextElement.TagName == "P");

                    return aquisition.Trim();
                default:
                    return null;

            }

        }

        //no regex has worked out for me and im not going to try to formulate my own expression so I did it manually
        internal string GetImageSrc(string srcSet)
        {

            var qualityLink = srcSet?.Split(',').ElementAtOrDefault(1)?.Trim();

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

            var artistTitleIndex = dom.Children.ToList().IndexOf(artistTitle) + 1;
            return dom.Children[artistTitleIndex].FirstElementChild.TextContent;

        }

        //web scraping makes me want to shoot myself :D
        internal string GetFullArt(IElement dom)
        {

            var galleryTitle = dom.GetElementsByTagName("h2").FirstOrDefault(element => element.TextContent.Contains("Gallery"));

            if (galleryTitle == null)
                return null;

            //var childrenList = dom.Children.ToList();
            //var beginIndex = childrenList.IndexOf(galleryTitle);
            //childrenList = childrenList.GetRange(beginIndex, childrenList.Count - beginIndex);
            //var checkImage = childrenList.FirstOrDefault(element => element.ClassName == "thumb tleft");
            var checkImage = dom.GetElementsByClassName("thumb tleft").FirstOrDefault();

            if (checkImage == null)
            {

                //checkImage = childrenList.FirstOrDefault(element => element.ClassName == "thumb tright");
                checkImage = dom.GetElementsByClassName("thumb tright").FirstOrDefault();

                if (checkImage == null)
                    return null;

            }

            var img = checkImage.GetElementsByTagName("img").FirstOrDefault();

            if (img == null)
                return null;

            var srcSet = img.GetAttribute("srcset");

            if (srcSet == null)
                return img.GetAttribute("src");

            var array = srcSet.Split(',');
            var qualityLink = array.ElementAtOrDefault(1)?.Trim();

            if (string.IsNullOrEmpty(qualityLink))
                return srcSet;
            else
                return qualityLink.Split(' ').First();

        }

        internal void GetListOfCards()
        {

            Log("Amassing card list...");

            var counter = 1;

            Parallel.ForEach(CardLists, POptions, expansion =>
            {

                var dom = _parser.Parse(_web.GetStreamAsync(expansion).Result);
                var mainDom = dom.GetElementById("mw-content-text");
                var tables = mainDom.GetElementsByTagName("table");
                var collectibleCards = tables[0].GetElementsByTagName("tbody").First().Children.Where(element => !element.TextContent.Contains("Description") && !element.TextContent.Contains("Showing all"));
                var uncollectibleCards = expansion == "/Debug_card" ?
                collectibleCards :
                tables[1].GetElementsByTagName("tbody").First().Children.Where(element => !element.TextContent.Contains("Description") && !element.TextContent.Contains("Showing all"));

                foreach (var card in collectibleCards)
                {

                    if (expansion == "/Debug_card")
                        break;

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

            foreach (var link in AlphabeticalSortedCards)
            {

                var dom = _parser.Parse(_web.GetStreamAsync(link).Result);
                var mainDom = dom.GetElementById("mw-pages");

                var cards = mainDom.GetElementsByClassName("mw-category-group")
                    .SelectMany(element => element.GetElementsByTagName("li"))
                    .Select(element => element.FirstElementChild.GetAttribute("href"));

                var moreCards = cards.Where(url => url != null
                && !url.Contains("Boilerplates")
                && !_uncollectibles.AsParallel().Contains(url)
                && !_collectibles.AsParallel().Contains(url)).ToList();

                moreCards.AsParallel().ForAll(url =>
                {

                    _uncollectibles.Add(url);
                    InlineLog($"{Interlocked.Increment(ref counter)}");

                });

            }

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
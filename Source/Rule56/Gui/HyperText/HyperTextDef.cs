using System;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using Verse;
namespace CombatAI.Gui
{
    [StaticConstructorOnStartup]
    public class HyperTextDef : Def
    {
        // Ensure a public parameterless ctor exists for reflection-based loaders
        public HyperTextDef() { }

        // NOTE: avoid adding other members named `LoadDataFromXmlCustom` (static overloads)
        // because some engine reflection lookups will throw AmbiguousMatchException
        // if multiple methods with that name exist on the type.
        [Unsaved(allowLoading: false)]
        private readonly List<Action<Listing_Collapsible>> actions = new List<Action<Listing_Collapsible>>();

        // Public string that will receive the serialized content when defs use
        // a CDATA string. Using `contentString` avoids the engine attempting to
        // parse nested XML nodes into a single string which fails when child
        // elements are present.
        public string contentString;

        public void DrawParts(Listing_Collapsible collapsible)
        {
            foreach (Action<Listing_Collapsible> part in actions)
            {
                part(collapsible);
            }
        }

        public void LoadDataFromXmlCustom(XmlNode xmlRoot)
        {
            try
            {
                // The DirectXml loader will attempt to map element names to
                // public fields first. We intentionally do not expose a
                // `content` string field so we can safely handle the
                // structured <content> node (with child elements) here.
                // If the default loader populated `contentString`, parse it.
                if (!string.IsNullOrEmpty(contentString))
                {
                    try
                    {
                        var docField = new XmlDocument();
                        docField.LoadXml($"<root>{contentString}</root>");
                        ParseXmlContent(docField.DocumentElement);
                    }
                    catch (Exception exField)
                    {
                        Log.Error($"HyperTextDef: failed to parse contentString for {defName}: {exField}");
                    }
                }

                foreach (XmlNode node in xmlRoot.ChildNodes)
                {
                    if (node.Name == "content")
                    {
                        // Backwards compatibility: parse structured <content> nodes
                        // if any defs still use them.
                        ParseXmlContent(node);
                    }
                    else if (node.Name == "defName")
                    {
                        defName = node.InnerText;
                    }
                }
            }
            catch (Exception er)
            {
                Log.Error(er.ToString());
            }
        }

        private void ParseXmlContent(XmlNode xmlRoot)
        {
            foreach (XmlNode node in xmlRoot.ChildNodes)
            {
                if (node is XmlElement element)
                {
                    if (element.Name == "p")
                    {
                        ParseTextXmlNode(element);
                    }
                    else if (element.Name == "img")
                    {
                        ParseMediaNode(element);
                    }
                    else if (element.Name == "gap")
                    {
                        ParseGapNode(element);
                    }
                }
            }
        }

        private void ParseTextXmlNode(XmlElement element)
        {
            XmlAttribute fontSize = element.Attributes["fontSize"];
            if (fontSize == null || !Enum.TryParse(fontSize.Value, true, out GUIFontSize size))
            {
                size = GUIFontSize.Small;
            }
            XmlAttribute textAnchor = element.Attributes["textAnchor"];
            if (textAnchor == null || !Enum.TryParse(textAnchor.Value, true, out TextAnchor anchor))
            {
                anchor = TextAnchor.UpperLeft;
            }
            string text = element.InnerText.Replace('[', '<').Replace(']', '>');

            void Action(Listing_Collapsible collapsible)
            {
                GUIUtility.ExecuteSafeGUIAction(() =>
                {
                    GUIFont.Anchor = anchor;
                    GUIFont.Font   = size;
                    collapsible.Lambda(text.GetTextHeight(collapsible.Rect.width + 20) + 5, rect =>
                    {
                        GUIFont.Anchor = anchor;
                        GUIFont.Font   = size;
                        Widgets.Label(rect, text);
                    });
                });
            }

            actions.Add(Action);
        }

        private void ParseGapNode(XmlElement element)
        {
            XmlAttribute gapHeight = element.Attributes["height"];
            if (gapHeight == null || !int.TryParse(gapHeight.Value, out int height))
            {
                height = 1;
            }

            void Action(Listing_Collapsible collapsible)
            {
                collapsible.Gap(height);
            }

            actions.Add(Action);
        }

        private void ParseMediaNode(XmlElement element)
        {
            string       path      = element.Attributes["path"].Value;
            string       heightStr = null;
            XmlAttribute imgHeight = element.Attributes["height"];
            if (imgHeight != null)
            {
                heightStr = imgHeight.Value;
            }
            int index = actions.Count;
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                Texture2D texture = ContentFinder<Texture2D>.Get(path);
                int       width   = texture.width;
                if (heightStr == null || !int.TryParse(heightStr, out int height))
                {
                    height = texture.height;
                }

                void Action(Listing_Collapsible collapsible)
                {
                    collapsible.Lambda(height, rect =>
                    {
                        Widgets.DrawTextureFitted(rect, texture, 1.0f);
                    });
                }

                actions[index] = Action;
            });
            actions.Add(null);
        }
    }
}

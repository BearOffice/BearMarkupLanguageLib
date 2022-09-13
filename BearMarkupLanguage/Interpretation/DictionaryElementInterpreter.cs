﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BearMarkupLanguage.Elements;
using BearMarkupLanguage.Helpers;
using BearMarkupLanguage.Interpretation.Helpers;
using BearMarkupLanguage.Text;

namespace BearMarkupLanguage.Interpretation;

internal class DictionaryElementInterpreter : IInterpreter
{
    public ElementResult Interprete(string[] lines, ParseMode mode)
    {
        return mode switch
        {
            ParseMode.Collapse => CollapsedInterprete(lines[0]),
            ParseMode.Expand => ExpandedInterprete(lines),
            _ => throw new NotImplementedException(),
        };
    }

    private static ElementResult CollapsedInterprete(string line)
    {
        line = line.TrimEnd();

        if (CollapsedElementHelper.FindElementEndIndex(line, 0, ID.CollapsedDicNodeR) != line.Length - 1)
            return ElementResult.Fail(new InvalidFormatExceptionArgs
            {
                LineIndex = 0,
                CharIndex = 0,
                Message = "The format of this element is not valid."
            }); ;

        var tempDir = new OrderedDictionary<BasicElement, IBaseElement>();

        var isSplited = true;
        var isEmpty = true;
        var tempKey = default(BasicElement);
        var valueEntry = false;
        for (var i = 1; i < line.Length - 1; i++)
        {
            var ch = line[i];
            if (ch.IsWhiteSpace()) continue;

            if (CollapsedElementHelper.IsValidStartNode(ch))
            {
                if (!isSplited) return ElementResult.Fail(new InvalidFormatExceptionArgs
                {
                    LineIndex = 0,
                    CharIndex = i,
                    Message = "Missing split symbol."
                });
                if (tempKey is null && (ch == ID.CollapsedListNodeL || ch == ID.CollapsedDicNodeL))
                    return ElementResult.Fail(new InvalidFormatExceptionArgs
                    {
                        LineIndex = 0,
                        CharIndex = i,
                        Message = "Invalid key type."
                    });
                if (tempKey is not null && !valueEntry)
                    return ElementResult.Fail(new InvalidFormatExceptionArgs
                    {
                        LineIndex = 0,
                        CharIndex = i,
                        Message = "Missing keyvaluepair's split symbol."
                    });

                var index = CollapsedElementHelper.FindElementEndIndex(line, i);
                if (index == -1) return ElementResult.Fail(new InvalidFormatExceptionArgs
                {
                    LineIndex = 0,
                    CharIndex = i,
                    Message = "Bracket not closed."
                });

                string content;
                if (ch == ID.CollapsedBasicElementNode)
                    content = line[(i + 1)..index];  // remove "" for basic element
                else
                    content = line[i..(index + 1)];

                var result = CollapsedElementHelper.GetInterpreterWithNodeSymbol(ch)
                                                   .Interprete(new[] { content }, ParseMode.Collapse);
                if (!result.IsSuccess) return ElementResult.PassToParent(result, 0, i);

                if (tempKey is null)
                {
                    tempKey = (BasicElement)result.Value;
                }
                else
                {
                    tempDir.Add(tempKey, result.Value);
                    tempKey = null;
                    valueEntry = false;
                    isSplited = false;
                    isEmpty = false;
                }

                i = index;
            }
            else if (ch == ID.EmptySymbol[0])
            {
                if (line[i..].StartsWith(ID.EmptySymbol))
                {
                    if (!isSplited) return ElementResult.Fail(new InvalidFormatExceptionArgs
                    {
                        LineIndex = 0,
                        CharIndex = i,
                        Message = "Missing split symbol."
                    });
                    if (tempKey is null) return ElementResult.Fail(new InvalidFormatExceptionArgs
                    {
                        LineIndex = 0,
                        CharIndex = i,
                        Message = "Invalid key type. Key cannot be null."
                    });
                    if (tempKey is not null && !valueEntry)
                        return ElementResult.Fail(new InvalidFormatExceptionArgs
                        {
                            LineIndex = 0,
                            CharIndex = i,
                            Message = "Missing key symbol."
                        });

                    var result = new EmptyElementInterpreter().Interprete(null, ParseMode.Collapse);
                    if (!result.IsSuccess) return ElementResult.PassToParent(result, 0, i);

                    tempDir.Add(tempKey, result.Value);
                    i = i + ID.EmptySymbol.Length - 1;
                    tempKey = null;
                    valueEntry = false;
                    isSplited = false;
                    isEmpty = false;
                }
                else
                {
                    return ElementResult.Fail(new InvalidFormatExceptionArgs
                    {
                        LineIndex = 0,
                        CharIndex = i,
                        Message = "Unknown character."
                    });
                }
            }
            else if (ch == ID.CollapsedElementSplitSymbol)
            {
                if (valueEntry) return ElementResult.Fail(new InvalidFormatExceptionArgs
                {
                    LineIndex = 0,
                    CharIndex = i,
                    Message = "Missing value."
                });

                if (isSplited) return ElementResult.Fail(new InvalidFormatExceptionArgs
                {
                    LineIndex = 0,
                    CharIndex = i,
                    Message = "Unnecessary split symbol."
                });
                else
                    isSplited = true;
            }
            else if (ch == ID.Key)
            {
                if (valueEntry) return ElementResult.Fail(new InvalidFormatExceptionArgs
                {
                    LineIndex = 0,
                    CharIndex = i,
                    Message = "Unnecessary key symbol."
                });

                if (tempKey is null && !valueEntry)
                    return ElementResult.Fail(new InvalidFormatExceptionArgs
                    {
                        LineIndex = 0,
                        CharIndex = i,
                        Message = "Missing key."
                    });
                else
                    valueEntry = true;
            }
            else
            {
                return ElementResult.Fail(new InvalidFormatExceptionArgs
                {
                    LineIndex = 0,
                    CharIndex = i,
                    Message = "Unknown character."
                });
            }
        }

        if (valueEntry) return ElementResult.Fail(new InvalidFormatExceptionArgs
        {
            LineIndex = 0,
            Message = "Missing value."
        });
        if (!isEmpty && isSplited) return ElementResult.Fail(new InvalidFormatExceptionArgs
        {
            LineIndex = 0,
            Message = "Unnecessary split symbol."
        });

        return ElementResult.Success(new DictionaryElement(tempDir));
    }

    private static ElementResult ExpandedInterprete(string[] lines)
    {
        var refLines = new ReferList<string>(lines);
        var tempDic = new OrderedDictionary<BasicElement, IBaseElement>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (ContextInterpreter.IsBlankLine(lines[i])) continue;
            if (!IsDictionaryNode(lines[i]))
                return ElementResult.Fail(new InvalidFormatExceptionArgs
                {
                    LineIndex = i,
                    CharIndex = 0,
                    Message = "Invalid line."
                });

            var key = GetKey(lines[i], out var idIndex);
            if (key is null)
                return ElementResult.Fail(new InvalidFormatExceptionArgs
                {
                    LineIndex = i,
                    CharIndex = 0,
                    Message = "Key cannot be empty or white space in expanded dictionary."
                });

            // key.Length + id's length
            if (idIndex + 1 == lines[i].Length)
                refLines[i] = "";
            else
                refLines[i] = refLines[i][(idIndex + 1)..];

            var result = ContextInterpreter.ContentInterprete(refLines[i..], out var endAtIndex);
            if (!result.IsSuccess) 
                return ElementResult.PassToParent(result, i, 0);

            tempDic.Add(new BasicElement(key), result.Value);
            i += endAtIndex;
        }

        return ElementResult.Success(new DictionaryElement(tempDic));
    }

    private static bool IsDictionaryNode(string line)
    {
        return ContextInterpreter.IsKeyLine(line);
    }

    private static string GetKey(string line, out int idIndex)
    {
        idIndex = line.IndexOfWithEscape(ID.Key);
        var key = line[0..idIndex].TrimEnd().Unescape();

        if (key.IsNullOrWhiteSpace())
            return null;
        else
            return key;
    }
}
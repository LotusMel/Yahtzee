﻿using Microsoft.AspNet.SignalR;
using Yahtzee.Framework;
using System;
using System.Linq;
using Autofac;
using Website.Models;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Website.HubHelpers;

namespace Website
{
	public class YahtzeeHub : Hub
	{
		private static ConcurrentDictionary<string, GameStateModel> _stateDict = new ConcurrentDictionary<string, GameStateModel>();

		private readonly ILifetimeScope _hubScope;

		private readonly Func<IScoreSheet> _scoreSheetFactory;
		private readonly Func<IDiceCup> _diceCupFactory;


		// This isn't awesome but to control the lifetime scope of the hub's dependencies,
		// the root container needs to be passed in, similar to a service locator.
		// See http://autofac.readthedocs.org/en/latest/integration/signalr.html
		public YahtzeeHub(ILifetimeScope rootScope)
		{
			_hubScope = rootScope.BeginLifetimeScope();

			_scoreSheetFactory = _hubScope.Resolve<Func<IScoreSheet>>();
			_diceCupFactory = _hubScope.Resolve<Func<IDiceCup>>();
		}

		public void RollDice()
		{
			var state = GetOrCreateState();

			if (state.CurrentDiceCup.IsFinal())
			{
				return;
			}

			var rollResult = state.CurrentDiceCup.Roll();
			if (rollResult != null)
			{
				var rollData = new
				{
					dice = rollResult.Select(x => x.Value).ToList(),
					rollCount = state.CurrentDiceCup.RollCount,
					isFinal = state.CurrentDiceCup.IsFinal()
				};

				Clients.Caller.processRoll(rollData);
			}
			else
			{
				state.CurrentDiceCup = _diceCupFactory();
			}
		}

		public void TakeUpper(int number)
		{
			var state = GetOrCreateState();

			if (state.CurrentDiceCup.RollCount == 0)
			{
				return;
			}

			UpperSectionItem section = (UpperSectionItem)number;
			state.ScoreSheet.RecordUpperSection(section, state.CurrentDiceCup);

			var score = GetScoreForUpperSection(section, state.ScoreSheet);

			state.CurrentDiceCup = _diceCupFactory();

			var isUpperSectionComplete = state.ScoreSheet.IsUpperSectionComplete;
			int? upperSectionScore = null;
			int? upperSectionBonus = null;
			int? upperSectionTotal = null;
			if (isUpperSectionComplete)
			{
				upperSectionScore = state.ScoreSheet.UpperSectionTotal;
				upperSectionBonus = state.ScoreSheet.UpperSectionBonus;
				upperSectionTotal = state.ScoreSheet.UpperSectionTotalWithBonus;
			}

			bool isScoreSheetComplete = state.ScoreSheet.IsScoreSheetComplete;
			int? grandTotal = null;
			if (state.ScoreSheet.IsScoreSheetComplete)
			{
				grandTotal = state.ScoreSheet.GrandTotal;
			}

			Clients.Caller.setUpper(new
			{
				upperNum = number,
				score = score,
				isUpperSectionComplete = isUpperSectionComplete,
				upperSectionScore = upperSectionScore,
				upperSectionBonus = upperSectionBonus,
				upperSectionTotal = upperSectionTotal,
				isScoreSheetComplete = isScoreSheetComplete,
				grandTotal = grandTotal
			});
		}

		public void TakeLower(string name)
		{
			var state = GetOrCreateState();

			if (state.CurrentDiceCup.RollCount == 0)
			{
				return;
			}

			int? score = LowerSectionScorer.Score[name](state.ScoreSheet, state.CurrentDiceCup);

			state.CurrentDiceCup = _diceCupFactory();
			bool isLowerSectionComplete = state.ScoreSheet.IsLowerSectionComplete;
			int? lowerSectionTotal = null;
			if (isLowerSectionComplete)
			{
				lowerSectionTotal = state.ScoreSheet.LowerSectionTotal;
			}
			bool isScoreSheetComplete = state.ScoreSheet.IsScoreSheetComplete;
			int? grandTotal = null;
			if (state.ScoreSheet.IsScoreSheetComplete)
			{
				grandTotal = state.ScoreSheet.GrandTotal;
			}

			Clients.Caller.setLower(new
			{
				name = name,
				score = score.Value,
				isLowerSectionComplete = isLowerSectionComplete,
				lowerSectionTotal = lowerSectionTotal,
				isScoreSheetComplete = isScoreSheetComplete,
				grandTotal = grandTotal
			});
		}

		public void ToggleHoldDie(int index)
		{
			var state = GetOrCreateState();

			if (state.CurrentDiceCup.IsFinal() || state.CurrentDiceCup.RollCount == 0)
			{
				return;
			}

			if (state.CurrentDiceCup.Dice[index].State == DieState.Held)
			{
				state.CurrentDiceCup.Unhold(index);
			}
			else
			{
				state.CurrentDiceCup.Hold(index);
			}

			Clients.Caller.toggleHoldDie(new
			{
				index = index,
				dieState = state.CurrentDiceCup.Dice[index].State.ToString()
			});
		}

		private int GetScoreForUpperSection(UpperSectionItem section, IScoreSheet scoreSheet)
		{
			switch (section)
			{
				case UpperSectionItem.Ones:
					return scoreSheet.Ones.Value;
				case UpperSectionItem.Twos:
					return scoreSheet.Twos.Value;
				case UpperSectionItem.Threes:
					return scoreSheet.Threes.Value;
				case UpperSectionItem.Fours:
					return scoreSheet.Fours.Value;
				case UpperSectionItem.Fives:
					return scoreSheet.Fives.Value;
				case UpperSectionItem.Sixes:
					return scoreSheet.Sixes.Value;
			}

			return 0;
		}

		private GameStateModel GetOrCreateState()
		{
			var currentConnectionId = Context.ConnectionId;
			GameStateModel state;
			var stateExists = _stateDict.TryGetValue(currentConnectionId, out state);

			if (!stateExists)
			{
				state = new GameStateModel
				{
					ConnectionId = currentConnectionId,
					ScoreSheet = _scoreSheetFactory(),
					CurrentDiceCup = _diceCupFactory()
				};
				_stateDict.AddOrUpdate(currentConnectionId, state, (key, existingVal) => state);
			}

			return state;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && _hubScope != null)
			{
				_hubScope.Dispose();
			}

			base.Dispose(disposing);
		}
	}
}
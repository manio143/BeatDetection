/*
 *BeatDetektor.cs
 *
 *  BeatDetektor - CubicFX Visualizer Beat Detection & Analysis Algorithm
 *
 *  Created by Charles J. Cliffe <cj@cubicproductions.com> on 09-11-30.
 *  Copyright 2009 Charles J. Cliffe. All rights reserved.
 *  
 *  Translated from C++ to C# by Marian Dziubiak (github.com/manio143)
 *
 *  BeatDetektor is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU Lesser General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  BeatDetektor is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU Lesser General Public License for more details.
 *
 *  You should have received a copy of the GNU Lesser General Public License
 *  along with this program.  If not, see <http://www.gnu.org/licenses/>.
 *
 *  Visit www.cubicvr.org for BeatDetektor forum and support.
 *
*/

/* 
 BeatDetektor class

 Theory:

 Trigger detection is performed using a trail of moving averages, 
 
 The FFT input is broken up into 128 ranges and averaged, each range has two moving 
 averages that tail each other at a rate of (1.0 / BD_DETECTION_RATE) seconds.  

 Each time the moving average for a range exceeds it's own tailing average by:

 (moving_average[range] * BD_DETECTION_FACTOR >= moving_average[range])

 if this is true there's a rising edge and a detection is flagged for that range. 
 Next a trigger gap test is performed between rising edges and timestamp recorded. 

 If the gap is larger than our BPM window (in seconds) then we can discard it and
 reset the timestamp for a new detection -- but only after checking to see if it's a 
 reasonable match for 2* the current detection in case it's only triggered every
 other beat. Gaps that are lower than the BPM window are ignored and the last 
 timestamp will not be reset.  

 Gaps that are within a reasonable window are run through a quality stage to determine 
 how 'close' they are to that channel's current prediction and are incremented or 
 decremented by a weighted value depending on accuracy. Repeated hits of low accuracy 
 will still move a value towards erroneous detection but it's quality will be lowered 
 and will not be eligible for the gap time quality draft.
 
 Once quality has been assigned ranges are reviewed for good match candidates and if 
 BD_MINIMUM_CONTRIBUTIONS or more ranges achieve a decent ratio (with a factor of 
 BD_QUALITY_TOLERANCE) of contribution to the overall quality we take them into the 
 contest round.  Note that the contest round  won't run on a given process() call if 
 the total quality achieved does not meet or exceed BD_QUALITY_TOLERANCE.
  
 Each time through if a select draft of BPM ranges has achieved a reasonable quality 
 above others it's awarded a value in the BPM contest.  The BPM contest is a hash 
 array indexed by an integer BPM value, each draft winner is awarded BD_QUALITY_REWARD.

 Finally the BPM contest is examined to determine a leader and all contest entries 
 are normalized to a total value of BD_FINISH_LINE, whichever range is closest to 
 BD_FINISH_LINE at any given point is considered to be the best guess however waiting 
 until a minimum contest winning value of about 20.0-25.0 will provide more accurate 
 results.  Note that the 20-25 rule may vary with lower and higher input ranges. 
 A winning value that exceeds 40 or hovers around 60 (the finish line) is pretty much
 a guaranteed match.


 Configuration Kernel Notes:

 The majority of the ratios and values have been reverse-engineered from my own  
 observation and visualization of information from various aspects of the detection 
 triggers; so not all parameters have a perfect definition nor perhaps the best value yet.
 However despite this it performs very well; I had expected several more layers 
 before a reasonable detection would be achieved. Comments for these parameters will be 
 updated as analysis of their direct effect is explored.


 Input Restrictions:

 bpm_maximum must be within the range of (bpm_minimum*2)-1
 i.e. minimum of 50 must have a maximum of 99 because 50*2 = 100

*/

using System;
using System.Collections.Generic;
using System.Linq;

internal class BeatDetektor
{
	public const int BD_DETECTION_RANGES = 128;
	public const float BD_DETECTION_RATE = 12.0f;
	public const float BD_DETECTION_FACTOR = 0.925f;

	public const float BD_QUALITY_TOLERANCE = 0.96f;
	public const float BD_QUALITY_DECAY = 0.95f;
	public const float BD_QUALITY_REWARD = 7.0f;
	public const float BD_QUALITY_STEP = 0.1f;
	public const float BD_FINISH_LINE = 60.0f;
	public const int BD_MINIMUM_CONTRIBUTIONS = 6;
	public const int REWARD_VALS = 7;

	public float BPM_MIN;
	public float BPM_MAX;

	public float current_bpm;
	public float winning_bpm;
	public float winning_bpm_lo;
	public float win_val;
	public int win_bpm_int;
	public float win_val_lo;
	public int win_bpm_int_lo;

	public float bpm_predict;

	public bool is_erratic;
	public float bpm_offset;
	public float last_timer;
	public float last_update;
	public float total_time;

	public float bpm_timer;
	public int beat_counter;
	public int half_counter;
	public int quarter_counter;
	public float detection_factor;
	//	float quality_minimum,
	public float quality_reward, quality_decay, detection_rate, finish_line;
	public int minimum_contributions;
	public float quality_total, quality_avg, ma_quality_lo, ma_quality_total, ma_quality_avg, maa_quality_avg;

	// current average (this sample) for range n
	public float[] a_freq_range = new float[BD_DETECTION_RANGES];
	// moving average of frequency range n
	public float[] ma_freq_range = new float[BD_DETECTION_RANGES];
	// moving average of moving average of frequency range n
	public float[] maa_freq_range = new float[BD_DETECTION_RANGES];
	// timestamp of last detection for frequecy range n
	public float[] last_detection = new float[BD_DETECTION_RANGES];

	// moving average of gap lengths
	public float[] ma_bpm_range = new float[BD_DETECTION_RANGES];
	// moving average of moving average of gap lengths
	public float[] maa_bpm_range = new float[BD_DETECTION_RANGES];

	// range n quality attribute, good match = quality+, bad match = quality-, min = 0
	public float[] detection_quality = new float[BD_DETECTION_RANGES];

	// current trigger state for range n
	public bool[] detection = new bool[BD_DETECTION_RANGES];

	public Dictionary<int, float> bpm_contest = new Dictionary<int, float>();   // 1/10th
	public Dictionary<int, float> bpm_contest_lo = new Dictionary<int, float>(); // 1/1

	public BeatDetektor(float BPM_MIN_in = 100.0f, float BPM_MAX_in = 200.0f)
	{

		//	quality_minimum = BD_QUALITY_MINIMUM;
		quality_reward = BD_QUALITY_REWARD;
		detection_rate = BD_DETECTION_RATE;
		finish_line = BD_FINISH_LINE;
		minimum_contributions = BD_MINIMUM_CONTRIBUTIONS;
		detection_factor = BD_DETECTION_FACTOR;
		quality_total = 1.0f;
		quality_avg = 1.0f;
		quality_decay = BD_QUALITY_DECAY;
		ma_quality_avg = 0.001f;
		ma_quality_lo = 1.0f;
		ma_quality_total = 1.0f;

		BPM_MIN = BPM_MIN_in;
		BPM_MAX = BPM_MAX_in;
		reset();
	}

	public void reset(bool reset_freq = true)
	{
		for (int i = 0; i < BD_DETECTION_RANGES; i++)
		{
			//			ma_bpm_range[i] = maa_bpm_range[i] = 60.0/(float)(BPM_MIN + (1.0+sin(8.0*M_PI*((float)i/(float)BD_DETECTION_RANGES))/2.0)*((BPM_MAX-BPM_MIN)/2));			
			ma_bpm_range[i] = maa_bpm_range[i] = 60.0f / (float)(BPM_MIN + 5) + ((60.0f / (float)(BPM_MAX - 5) - 60.0f / (float)(BPM_MIN + 5)) * ((float)i / (float)BD_DETECTION_RANGES));
			if (reset_freq)
			{
				a_freq_range[i] = ma_freq_range[i] = maa_freq_range[i] = 0;
			}
			last_detection[i] = 0;
			detection_quality[i] = 0;
			detection[i] = false;

		}

		total_time = 0;
		maa_quality_avg = 500.0f;
		bpm_offset = bpm_timer = last_update = last_timer = winning_bpm = current_bpm = win_val = win_bpm_int = 0;
		bpm_contest.Clear();
		bpm_contest_lo.Clear();
	}

	public void process(float timer_seconds, Span<float> fft_data)
	{
		if (last_timer == 0) { last_timer = timer_seconds; return; }    // ignore 0 start time

		if (timer_seconds < last_timer) { reset(); return; }

		float timestamp = timer_seconds;

		last_update = timer_seconds - last_timer;
		last_timer = timer_seconds;

		total_time += last_update;

		int range_step = (fft_data.Length / BD_DETECTION_RANGES);
		int range = 0;
		int i, x;
		float v;

		float bpm_floor = 60.0f / BPM_MAX;
		float bpm_ceil = 60.0f / BPM_MIN;

		// Always false?
		// if (current_bpm != current_bpm) current_bpm = 0;

		for (x = 0; x < fft_data.Length; x += range_step)
		{
			a_freq_range[range] = 0;

			// accumulate frequency values for this range
			for (i = x; i < x + range_step; i++)
			{
				v = Math.Abs(fft_data[i]);
				a_freq_range[range] += v;
			}

			// average for range
			a_freq_range[range] /= range_step;

			// two sets of averages chase this one at a 

			// moving average, increment closer to a_freq_range at a rate of 1.0 / detection_rate seconds
			ma_freq_range[range] -= (ma_freq_range[range] - a_freq_range[range]) * last_update * detection_rate;
			// moving average of moving average, increment closer to ma_freq_range at a rate of 1.0 / detection_rate seconds
			maa_freq_range[range] -= (maa_freq_range[range] - ma_freq_range[range]) * last_update * detection_rate;
			


			// if closest moving average peaks above trailing (with a tolerance of BD_DETECTION_FACTOR) then trigger a detection for this range 
			bool det = (ma_freq_range[range] * detection_factor >= maa_freq_range[range]);

			// compute bpm clamps for comparison to gap lengths

			// clamp detection averages to input ranges
			if (ma_bpm_range[range] > bpm_ceil) ma_bpm_range[range] = bpm_ceil;
			if (ma_bpm_range[range] < bpm_floor) ma_bpm_range[range] = bpm_floor;
			if (maa_bpm_range[range] > bpm_ceil) maa_bpm_range[range] = bpm_ceil;
			if (maa_bpm_range[range] < bpm_floor) maa_bpm_range[range] = bpm_floor;

			bool rewarded = false;

			// new detection since last, test it's quality
			if (!detection[range] && det)
			{
				// calculate length of gap (since start of last trigger)
				float trigger_gap = timestamp - last_detection[range];

				float[] reward_tolerances = { 0.001f, 0.005f, 0.01f, 0.02f, 0.04f, 0.08f, 0.10f };
				float[] reward_multipliers = { 20.0f, 10.0f, 8.0f, 1.0f, 1.0f / 2.0f, 1.0f / 4.0f, 1.0f / 8.0f };

				// trigger falls within acceptable range, 
				if (trigger_gap < bpm_ceil && trigger_gap > (bpm_floor))
				{
					// compute gap and award quality

					for (i = 0; i < REWARD_VALS; i++)
					{
						if (Math.Abs(ma_bpm_range[range] - trigger_gap) < ma_bpm_range[range] * reward_tolerances[i])
						{
							detection_quality[range] += quality_reward * reward_multipliers[i];
							rewarded = true;
						}
					}


					if (rewarded)
					{
						last_detection[range] = timestamp;
					}
				}
				else if (trigger_gap >= bpm_ceil) // low quality, gap exceeds maximum time
				{
					// test for 2* beat
					trigger_gap /= 2.0f;
					// && Math.Abs((60.0/trigger_gap)-(60.0/ma_bpm_range[range])) < 50.0
					if (trigger_gap < bpm_ceil && trigger_gap > (bpm_floor)) for (i = 0; i < REWARD_VALS; i++)
						{
							if (Math.Abs(ma_bpm_range[range] - trigger_gap) < ma_bpm_range[range] * reward_tolerances[i])
							{
								detection_quality[range] += quality_reward * reward_multipliers[i];
								rewarded = true;
							}
						}

					if (!rewarded) trigger_gap *= 2.0f;

					// start a new gap test, next gap is guaranteed to be longer
					last_detection[range] = timestamp;
				}


				float qmp = (detection_quality[range] / quality_avg) * BD_QUALITY_STEP;
				if (qmp > 1.0)
				{
					qmp = 1.0f;
				}

				if (rewarded)
				{
					ma_bpm_range[range] -= (ma_bpm_range[range] - trigger_gap) * qmp;
					maa_bpm_range[range] -= (maa_bpm_range[range] - ma_bpm_range[range]) * qmp;
				}
				else if (trigger_gap >= bpm_floor && trigger_gap <= bpm_ceil)
				{
					if (detection_quality[range] < quality_avg * BD_QUALITY_TOLERANCE && current_bpm != 0)
					{
						ma_bpm_range[range] -= (ma_bpm_range[range] - trigger_gap) * BD_QUALITY_STEP;
						maa_bpm_range[range] -= (maa_bpm_range[range] - ma_bpm_range[range]) * BD_QUALITY_STEP;
					}
					detection_quality[range] -= BD_QUALITY_STEP;
				}
				else if (trigger_gap >= bpm_ceil)
				{
					if (detection_quality[range] < quality_avg * BD_QUALITY_TOLERANCE && current_bpm != 0)
					{
						ma_bpm_range[range] -= (ma_bpm_range[range] - current_bpm) * 0.5f;
						maa_bpm_range[range] -= (maa_bpm_range[range] - ma_bpm_range[range]) * 0.5f;
					}
					detection_quality[range] -= quality_reward * BD_QUALITY_STEP;
				}

			}

			if ((!rewarded && timestamp - last_detection[range] > bpm_ceil) || ((det && Math.Abs(ma_bpm_range[range] - current_bpm) > bpm_offset)))
				detection_quality[range] -= detection_quality[range] * BD_QUALITY_STEP * quality_decay * last_update;

			// quality bottomed out, set to 0
			if (detection_quality[range] <= 0) detection_quality[range] = 0.001f;


			detection[range] = det;

			range++;
		}


		// total contribution weight
		quality_total = 0;

		// total of bpm values
		float bpm_total = 0;
		// number of bpm ranges that contributed to this test
		int bpm_contributions = 0;


		// accumulate quality weight total
		for (x = 0; x < BD_DETECTION_RANGES; x++)
		{
			quality_total += detection_quality[x];
		}

		// determine the average weight of each quality range
		quality_avg = quality_total / (float)BD_DETECTION_RANGES;


		ma_quality_avg += (quality_avg - ma_quality_avg) * last_update * detection_rate / 2.0f;
		maa_quality_avg += (ma_quality_avg - maa_quality_avg) * last_update;
		ma_quality_total += (quality_total - ma_quality_total) * last_update * detection_rate / 2.0f;

		ma_quality_avg -= 0.98f * ma_quality_avg * last_update * 3.0f;

		if (ma_quality_total <= 0) ma_quality_total = 1.0f;
		if (ma_quality_avg <= 0) ma_quality_avg = 1.0f;

		float avg_bpm_offset = 0.0f;
		float offset_test_bpm = current_bpm;
		Dictionary<int, float> draft = new Dictionary<int, float>();

		{
			for (x = 0; x < BD_DETECTION_RANGES; x++)
			{
				// if this detection range weight*tolerance is higher than the average weight then add it's moving average contribution 
				if (detection_quality[x] * BD_QUALITY_TOLERANCE >= ma_quality_avg)
				{
					if (maa_bpm_range[x] < bpm_ceil && maa_bpm_range[x] > bpm_floor)
					{
						bpm_total += maa_bpm_range[x];

						float draft_float = (float)Math.Round((60.0 / maa_bpm_range[x]) * 1000.0);

						draft_float = (Math.Abs(Math.Ceiling(draft_float) - (60.0 / current_bpm) * 1000.0) < (Math.Abs(Math.Floor(draft_float) - (60.0 / current_bpm) * 1000.0)))
							? (float)Math.Ceiling(draft_float / 10.0) : (float)Math.Floor(draft_float / 10.0);

						int draft_int = (int)(draft_float / 10.0);

						if (!draft.ContainsKey(draft_int))
							draft[draft_int] = 0;
						draft[draft_int] += (detection_quality[x] / quality_avg);
						bpm_contributions++;
						if (offset_test_bpm == 0.0) offset_test_bpm = maa_bpm_range[x];
						else
						{
							avg_bpm_offset += Math.Abs(offset_test_bpm - maa_bpm_range[x]);
						}

					}
				}
			}
		}

		// if we have one or more contributions that pass criteria then attempt to display a guess
		bool has_prediction = (bpm_contributions >= minimum_contributions) ? true : false;


		if (has_prediction)
		{

			int draft_winner = 0;
			int win_max = 0;

			foreach(var kvp in draft)
			{
				if (kvp.Value > win_max)
				{
					win_max = (int)kvp.Value;
					draft_winner = kvp.Key;
				}
			}

			bpm_predict = (60.0f / (float)(draft_winner / 10.0));

			avg_bpm_offset /= (float)bpm_contributions;
			bpm_offset = avg_bpm_offset;

			if (current_bpm == 0)
			{
				current_bpm = bpm_predict;
			}


			if (current_bpm != 0 && bpm_predict != 0) current_bpm -= (current_bpm - bpm_predict) * last_update; //*avg_bpm_offset*200.0;	
			if (/*current_bpm != current_bpm || */ current_bpm < 0) current_bpm = 0;


			// hold a contest for bpm to find the current mode

			float contest_max = 0;

			foreach(var kvp in bpm_contest)
			{
				if (contest_max < kvp.Value) contest_max = kvp.Value;
				if (kvp.Value > BD_FINISH_LINE / 2.0f)
				{
					var bpm_contest_lo_idx = (int)Math.Round(kvp.Key / 10.0);
					if (!bpm_contest_lo.ContainsKey(bpm_contest_lo_idx))
						bpm_contest_lo[bpm_contest_lo_idx] = 0;
					bpm_contest_lo[bpm_contest_lo_idx] += (kvp.Value / 10.0f) * last_update;
				}
			}

			var bpm_constest_indexes = bpm_contest.Keys.ToList();
			var bpm_constest_lo_indexes = bpm_contest_lo.Keys.ToList();
			// normalize to a finish line of BD_FINISH_LINE
			if (contest_max > finish_line)
			{
				foreach(var idx in bpm_constest_indexes)
				{
					bpm_contest[idx] = (bpm_contest[idx] / contest_max) * finish_line;
				}
			}

			contest_max = 0;

			foreach (var idx in bpm_constest_lo_indexes)
			{
				if (contest_max < bpm_contest_lo[idx]) contest_max = bpm_contest_lo[idx];
			}

			if (contest_max > finish_line)
			{
				foreach (var idx in bpm_constest_lo_indexes)
				{
					bpm_contest_lo[idx] = (bpm_contest_lo[idx] / contest_max) * finish_line;
				}
			}


			// decay contest values from last loop
			foreach (var idx in bpm_constest_indexes)
			{
				bpm_contest[idx] -= bpm_contest[idx] * (last_update / detection_rate);
			}

			// decay contest values from last loop
			foreach (var idx in bpm_constest_lo_indexes)
			{
				bpm_contest_lo[idx] -= bpm_contest_lo[idx] * (last_update / detection_rate);
			}


			bpm_timer += last_update;

			int winner = 0;
			int winner_lo = 0;

			// attempt to display the beat at the beat interval ;)
			if (bpm_timer > winning_bpm / 4.0f && current_bpm != 0)
			{
				if (winning_bpm != 0) while (bpm_timer > winning_bpm / 4.0) bpm_timer -= winning_bpm / 4.0f;

				// increment beat counter

				quarter_counter++;
				half_counter = quarter_counter / 2;
				beat_counter = quarter_counter / 4;

				// award the winner of this iteration
				var bpm_contest_idx = (int)Math.Round(60.0 / current_bpm * 10.0);
				if (!bpm_contest.ContainsKey(bpm_contest_idx))
					bpm_contest[bpm_contest_idx] = 0;
				bpm_contest[bpm_contest_idx] += quality_reward;

				win_val = 0;

				// find the overall winner so far
				foreach (var idx in bpm_constest_indexes)
				{
					if (win_val < bpm_contest[idx])
					{
						winner = idx;
						win_val = bpm_contest[idx];
					}
				}

				if (winner != 0)
				{
					win_bpm_int = winner;
					winning_bpm = 60.0f / (winner / 10.0f);
				}


				win_val_lo = 0;

				// find the overall winner so far
				foreach (var idx in bpm_constest_lo_indexes)
				{
					if (win_val_lo < bpm_contest_lo[idx])
					{
						winner_lo = idx;
						win_val_lo = bpm_contest_lo[idx];
					}
				}

				if (winner_lo != 0)
				{
					win_bpm_int_lo = winner_lo;
					winning_bpm_lo = 60.0f / winner_lo;
				}
			}
		}
	}
};



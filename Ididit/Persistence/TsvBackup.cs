﻿using CsvHelper;
using CsvHelper.Configuration;
using Ididit.App.Data;
using Ididit.Data;
using Ididit.Data.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ididit.Persistence;

internal class TsvBackup
{
    // https://stackoverflow.com/questions/66166584/csvhelper-custom-delimiter

    readonly CsvConfiguration _importConfig = new(CultureInfo.InvariantCulture)
    {
        //DetectDelimiter = true
        Delimiter = "\t",
        Quote = (char)1,
        Mode = CsvMode.NoEscape
    };

    readonly CsvConfiguration _exportConfig = new(CultureInfo.InvariantCulture)
    {
        Delimiter = "\t",
        Quote = (char)1,
        Mode = CsvMode.NoEscape
    };

    private readonly JsInterop _jsInterop;
    private readonly IRepository _repository;

    public TsvBackup(JsInterop jsInterop, IRepository repository)
    {
        _jsInterop = jsInterop;
        _repository = repository;
    }

    class CsvRow
    {
        public string Root = string.Empty;
        public string Category = string.Empty;
        public string Goal = string.Empty;
        public string Task = string.Empty;
        public Priority Priority = Priority.None;
        public string Interval = string.Empty;
        public string Duration = string.Empty;
    };

    public async Task ImportData(Stream stream)
    {
        // https://joshclose.github.io/CsvHelper/examples/reading/get-anonymous-type-records/

        using StreamReader streamReader = new(stream);

        using CsvReader csv = new(streamReader, _importConfig);

        // https://github.com/JoshClose/CsvHelper/blob/master/tests/CsvHelper.Tests/TypeConversion/IEnumerableConverterTests.cs

        IAsyncEnumerable<CsvRow> records = csv.GetRecordsAsync<CsvRow>();

        CategoryModel root;
        CategoryModel category;
        GoalModel goal;
        TaskModel task;

        await foreach (CsvRow record in records)
        {
            if (_repository.CategoryList.Any(c => c.Name == record.Root))
            {
                root = _repository.CategoryList.First(c => c.Name == record.Root);
            }
            else
            {
                root = _repository.CreateCategory(record.Root);

                await _repository.AddCategory(root);
            }

            if (root.CategoryList.Any(c => c.Name == record.Category))
            {
                category = root.CategoryList.First(c => c.Name == record.Category);
            }
            else
            {
                category = root.CreateCategory(_repository.NextCategoryId, record.Category);

                await _repository.AddCategory(category);
            }

            if (category.GoalList.Any(g => g.Name == record.Goal))
            {
                goal = category.GoalList.First(g => g.Name == record.Goal);
            }
            else
            {
                goal = category.CreateGoal(_repository.NextGoalId, record.Goal);

                await _repository.AddGoal(goal);
            }

            goal.Details += string.IsNullOrEmpty(goal.Details) ? record.Task : Environment.NewLine + record.Task;
            await _repository.UpdateGoal(goal.Id);

            TimeSpan desiredInterval = TimeSpan.Zero;
            TimeSpan? desiredDuration = null;
            TaskKind taskKind = TaskKind.Note;

            if (double.TryParse(record.Interval, NumberStyles.Any, CultureInfo.InvariantCulture, out double days))
            {
                desiredInterval = TimeSpan.FromDays(days);
                taskKind = TaskKind.RepeatingTask;
            }
            else if (string.Equals(record.Interval, "ASAP", StringComparison.OrdinalIgnoreCase))
            {
                desiredInterval = TimeSpan.Zero;
                taskKind = TaskKind.Task;
            }

            if (double.TryParse(record.Duration, NumberStyles.Any, CultureInfo.InvariantCulture, out double minutes))
            {
                desiredDuration = TimeSpan.FromMinutes(minutes);
            }

            task = goal.CreateTask(_repository.NextTaskId, record.Task, desiredInterval, record.Priority, taskKind, desiredDuration);

            await _repository.AddTask(task);
        }
    }

    public async Task ExportData(IDataModel data)
    {
        // https://joshclose.github.io/CsvHelper/examples/writing/write-anonymous-type-objects/

        List<CsvRow> records = new();

        foreach (CategoryModel root in data.CategoryList)
        {
            foreach (CategoryModel category in root.CategoryList)
            {
                foreach (GoalModel goal in category.GoalList)
                {
                    foreach (TaskModel task in goal.TaskList)
                    {
                        string interval = string.Empty;

                        if (task.IsTask)
                        {
                            interval = task.DesiredInterval.TotalDays > 0.0 ? task.DesiredInterval.TotalDays.ToString() : "ASAP";
                        }

                        string duration = task.DesiredDuration.HasValue && task.DesiredDuration.Value.TotalMinutes > 0.0 ? task.DesiredDuration.Value.TotalMinutes.ToString() : "";

                        records.Add(new CsvRow
                        { 
                            Root = root.Name,
                            Category = category.Name,
                            Goal = goal.Name,
                            Task = task.Name,
                            Priority = task.Priority,
                            Interval = interval,
                            Duration = duration
                        });
                    }
                }
            }
        }

        StringBuilder builder = new();

        using (StringWriter writer = new(builder))
        {
            using CsvWriter csv = new(writer, _exportConfig);

            csv.WriteRecords(records);
        }

        string tsv = builder.ToString();

        await _jsInterop.SaveAsUTF8("ididit.tsv", tsv);
    }
}

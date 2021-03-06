﻿using Spinx.Core;
using Spinx.Data.Infrastructure;
using Spinx.Data.Repository.Member;
using Spinx.Data.Repository.QuizAnswers;
using Spinx.Data.Repository.QuizQuestions;
using Spinx.Data.Repository.Quizs;
using Spinx.Domain.Members;
using Spinx.Services.Content;
using Spinx.Services.GeneralSettings;
using Spinx.Services.Members.Validators;
using Spinx.Services.QuizCategories.DTOs;
using System;
using System.Data.Entity;
using System.Linq;

namespace Spinx.Services.Members
{
    public interface IMemberQuizService
    {
        MemberQuizListDto SaveMemberQuizInit(int memberId, string slug);
        Result CheckQuizRunning(int memberId, int quizId);
        Result GetQuestionByMemberResult(int memberResultId, int sortOrder, int lastSortOrder);
        Result SaveAnswerByMember(MemberQuizAnswerDto dto);
        Result SubmitQuiz(int memberId, int memberResultId);

    }

    public class MemberQuizService : IMemberQuizService
    {
        private readonly IMemberRepository _memberRepository;
        private readonly IQuizRepository _quizRepository;
        private readonly IMemberQuizAnswerOptionsRepository _memberQuizAnswerOptionsRepository;
        private readonly IGeneralSettingService _generalSettingService;
        private readonly IQuizAnswerRepository _quizAnswerRepository;
        private readonly IMemberResultRepository _memberResultRepository;
        private readonly IMemberQuizAnswerRepository _memberQuizAnswerRepository;
        private readonly IQuizQuestionRepository _quizQuestionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly MemberEditProfileValidator _memberEditProfileValidator;


        public MemberQuizService(
                IMemberRepository memberRepository,
                IMemberResultRepository memberResultRepository,
                IMemberQuizAnswerRepository memberQuizAnswerRepository,
                IQuizQuestionRepository quizQuestionRepository,
                IQuizAnswerRepository quizAnswerRepository,
                IUnitOfWork unitOfWork,
                IQuizRepository quizRepository,
                IMemberQuizAnswerOptionsRepository memberQuizAnswerOptionsRepository,
                IGeneralSettingService generalSettingService,
                MemberEditProfileValidator memberEditProfileValidator)
        {
            _memberRepository = memberRepository;
            _quizRepository = quizRepository;
            _memberQuizAnswerOptionsRepository = memberQuizAnswerOptionsRepository;
            _generalSettingService = generalSettingService;
            _memberQuizAnswerRepository = memberQuizAnswerRepository;
            _memberResultRepository = memberResultRepository;
            _quizQuestionRepository = quizQuestionRepository;
            _quizAnswerRepository = quizAnswerRepository;
            _unitOfWork = unitOfWork;
            _memberEditProfileValidator = memberEditProfileValidator;

        }



        public MemberQuizListDto SaveMemberQuizInit(int memberId, string slug)
        {
            var result = new MemberQuizListDto();

            var quiz = _quizRepository.AsNoTracking.FirstOrDefault(w => w.Slug == slug && w.IsActive);
            if (quiz == null)
            {
                result.Success = false;
                result.Message = "Test not found.";
                return result;
            }


            if (!_memberResultRepository.AsNoTracking.Any(a => a.MemberId == memberId && a.QuizId == quiz.Id && a.EndTime == null))
            {

                var entity = new MemberResult()
                {
                    MemberId = memberId,
                    QuizId = quiz.Id,
                    CreatedAt = DateTime.Now,
                    StartTime = DateTime.Now
                };
                _memberResultRepository.Insert(entity);
                _unitOfWork.Commit();

                var totalNumberQuestionData = _generalSettingService.GetGeneralSetting("total-question-display");
                var totalNumberQuestion = string.IsNullOrWhiteSpace(totalNumberQuestionData)
                    ? 30
                    : Convert.ToInt32(totalNumberQuestionData);

                var questionList = _quizQuestionRepository.AsNoTracking.Where(x => x.QuizId == entity.QuizId).OrderBy(o => Guid.NewGuid()).Select(s => s.Id).Skip(0).Take(totalNumberQuestion).ToList();
                var i = 1;
                foreach (var questionId in questionList)
                {
                    var memberQuizEntity = new MemberQuizAnswer()
                    {
                        MemberResultId = entity.Id,
                        QuizQuestionId = questionId,
                        CreatedAt = DateTime.Now,
                        SortOrder = i
                    };

                    _memberQuizAnswerRepository.Insert(memberQuizEntity);
                    _unitOfWork.Commit();

                    var quizAnswerList = _quizAnswerRepository.AsNoTracking
                        .Where(w => w.QuizQuestionId == memberQuizEntity.QuizQuestionId)
                        .OrderBy(o => Guid.NewGuid()).Select(s => new { s.Id }).ToList();

                    var j = 1;
                    foreach (var quizAnswer in quizAnswerList)
                    {
                        var memberQuizAnswerOptions = new MemberQuizAnswerOptions()
                        {
                            MemberQuizAnswerId = memberQuizEntity.Id,
                            QuizAnswerId = quizAnswer.Id,
                            SortOrder = j
                        };
                        _memberQuizAnswerOptionsRepository.Insert(memberQuizAnswerOptions);
                        j++;
                    }
                    _unitOfWork.Commit();

                    i++;
                }


            }

            var memberResult = _memberResultRepository.AsNoTracking.FirstOrDefault(a => a.MemberId == memberId && a.QuizId == quiz.Id && a.EndTime == null);
            result.MemberResultId = memberResult.Id;
            result.StartTime = memberResult.StartTime;
            result.Success = true;
            result.SortOrder = 1;
            var memberQuiz = _memberQuizAnswerRepository.AsNoTracking.Where(w => w.MemberResultId == memberResult.Id);
            result.MemberQuizAnswerList = memberQuiz.OrderBy(o => o.SortOrder).ToList();
            var quizOrder = memberQuiz.Where(w => w.QuizAnswerId != null).OrderByDescending(o => o.UpdatedAt).FirstOrDefault();
            if (quizOrder != null)
                result.SortOrder = quizOrder.SortOrder;
            return result;
        }

        public Result CheckQuizRunning(int memberId, int quizId)
        {
            var result = new Result();
            result.Success = _memberResultRepository.AsNoTracking.Any(a =>
                a.MemberId == memberId && a.QuizId == quizId && a.EndTime == null);
            return result;
        }

        public Result GetQuestionByMemberResult(int memberResultId, int sortOrder, int lastSortOrder)
        {
            var result = new Result();

            var entity = _memberQuizAnswerRepository.AsNoTracking.FirstOrDefault(w =>
                w.MemberResultId == memberResultId && w.SortOrder == lastSortOrder);
            if (entity != null)
            {
                if (entity.QuizAnswerId == null && entity.IsAttempt == null)
                {
                    entity.IsAttempt = false;
                    _memberQuizAnswerRepository.Update(entity);
                    _unitOfWork.Commit();
                }
            }

            result.Data = _memberQuizAnswerRepository.AsNoTracking.Where(w => w.MemberResultId == memberResultId && w.SortOrder == sortOrder)
           .Select(s => new
           {
               questionId = s.QuizQestion.Id,
               memberQuizAnswerId = s.QuizAnswerId,
               question = s.QuizQestion.Question,
               sortOrder = s.SortOrder,
               quizAnswerId = s.QuizAnswerId,
               quizAnswer = s.MemberQuizAnswerOptions.Select(q => new { q.QuizAnswer.Id, q.QuizAnswer.Answer })

           }).FirstOrDefault();
            return result;
        }

        public Result SaveAnswerByMember(MemberQuizAnswerDto dto)
        {
            var result = new Result();
            var entity = _memberQuizAnswerRepository.AsNoTracking.FirstOrDefault(x => x.MemberResultId == dto.MemberResultId && x.QuizQuestionId == dto.QuizQuestionId);

            if (entity == null)
                return result.SetError("Something wrong.");

            if (dto.QuizAnswerId == null || dto.QuizAnswerId == 0)
                return result.SetError("Answer is Blank");

            entity.QuizAnswerId = dto.QuizAnswerId;
            entity.UpdatedAt = DateTime.Now;
            entity.IsAttempt = true;
            entity.IsRight = _quizAnswerRepository.AsNoTracking.FirstOrDefault(x => x.Id == entity.QuizAnswerId).IsCorrectAnswer;

            _memberQuizAnswerRepository.Update(entity);

            _unitOfWork.Commit();

            return result.SetSuccess(Messages.RecordSaved);
        }

        public Result SubmitQuiz(int memberId, int memberResultId)
        {
            var result = new Result();
            var entity = _memberResultRepository.AsNoTracking.FirstOrDefault(a => a.MemberId == memberId && a.Id == memberResultId && a.EndTime == null);
            if (entity == null)
                return result.SetError("Something wrong.");
            var TotalTimeMinute= _generalSettingService.GetGeneralSetting("total-time");

            var memberQuizAnswer = _memberQuizAnswerRepository.AsNoTracking.Where(x => x.MemberResultId == entity.Id);
            var totalquestion = memberQuizAnswer.Count();
            var totalRightAnswer = memberQuizAnswer.Where(x => x.IsRight).Count();
            entity.UpdatedAt = DateTime.Now;
            entity.EndTime = DateTime.Now;
            var startTime = entity.StartTime.AddMinutes(Convert.ToDouble(TotalTimeMinute));
            int res = DateTime.Compare(startTime, DateTime.Now);
            if (res < 0)
                entity.EndTime = startTime;
            
            entity.AttempedQues = memberQuizAnswer.Where(x => x.QuizAnswerId != null).Count();
            entity.Percentage = (totalRightAnswer * 100) / totalquestion;
            entity.Score = totalRightAnswer;
            _memberResultRepository.Update(entity);
            _unitOfWork.Commit();

            result.SetSuccess(Messages.RecordSaved);
            return result;
        }


    }
}
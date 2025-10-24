using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using Random = UnityEngine.Random;

public class AnimalController : MonoBehaviour
{
    private Animator _animator;
    private Vector3 _startPos;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _startPos = transform.position;
    }

    private void Start()
    {
        StartCoroutine(IIdleAndMove());
    }

    private IEnumerator IIdleAndMove()
    {
        _animator.Play("idle");
        yield return new WaitForSeconds(Random.Range(5, 10));
        Vector3 newPos = _startPos + new Vector3(Random.Range(-10f, 10f), 0, Random.Range(-10f, 10f));
        transform.LookAt(newPos);
        _animator.Play("walk");
        transform.DOMove(newPos, Vector3.Distance(newPos, transform.position) / 2f).OnComplete(() =>
        {
            StartCoroutine(IIdleAndMove());
        });
        yield break;
    }
}
